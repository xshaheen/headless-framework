// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;
using Headless.Messaging;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Headless.Caching;

/// <summary>
/// Two-tier hybrid cache combining a process-local L1 (<see cref="IInMemoryCache"/>) with a shared
/// distributed L2 (<see cref="IRemoteCache"/>), with cross-instance L1 invalidation via
/// <see cref="CacheInvalidationMessage"/> published over <c>IBus</c>.
/// </summary>
/// <remarks>
/// <para><b>Read path (GetOrAddAsync):</b></para>
/// <list type="number">
/// <item>L1 hit (fast, no network) → return.</item>
/// <item>L2 hit → populate L1 with the remaining L2 logical TTL capped at <see cref="HybridCacheOptions.DefaultLocalExpiration"/> → return.</item>
/// <item>Both miss → run factory (stampede-protected per-key), write L1 + L2, publish invalidation so peer L1s evict their stale copies.</item>
/// </list>
/// <para><b>Write/Invalidation path:</b></para>
/// <list type="number">
/// <item>Publish invalidation message first to narrow the stale-read window on peers.</item>
/// <item>Write or remove from L1 and L2.</item>
/// <item>Peers receive the message and drop their local copies.</item>
/// </list>
/// <para>
/// L2 faults degrade gracefully by default: reads fall back to L1 or a miss; factory-path L2 writes are
/// logged and dropped (or queued when <see cref="HybridCacheOptions.EnableAutoRecovery"/> is on).
/// Set <see cref="HybridCacheOptions.ReThrowDistributedCacheExceptions"/> to fail loud instead.
/// </para>
/// <para>
/// This class is <see cref="IAsyncDisposable"/>; dispose it to drain the auto-recovery queue and release
/// internal resources. DI-registered singletons are disposed automatically on host shutdown.
/// </para>
/// </remarks>
[PublicAPI]
public sealed partial class HybridCache(
    IInMemoryCache l1Cache,
    IRemoteCache l2Cache,
    IBus publisher,
    HybridCacheOptions cacheOptions,
    ILogger<HybridCache>? logger = null,
    TimeProvider? timeProvider = null,
    ICacheFactoryLockProvider? factoryLockProvider = null
) : ICache, IFactoryCacheStore, IBufferCache, IAsyncDisposable
{
    private readonly ILogger _logger = logger ?? NullLogger<HybridCache>.Instance;
    private readonly TimeProvider _timeProvider = timeProvider ?? TimeProvider.System;
    private readonly string _instanceId = cacheOptions.InstanceId ?? Guid.NewGuid().ToString("N");
    private readonly string? _cacheName = cacheOptions.CacheName;
    private readonly FactoryCacheCoordinator _coordinator = new(
        timeProvider ?? TimeProvider.System,
        logger,
        factoryLockProvider
    );

    private long _localCacheHits;
    private long _invalidateCacheCalls;
    private int _isDisposed;

    /// <inheritdoc />
    public CacheEntryOptions? DefaultEntryOptions { get; } = cacheOptions.DefaultEntryOptions;

    /// <summary>Gets the number of L1 cache hits.</summary>
    public long LocalCacheHits => Interlocked.Read(ref _localCacheHits);

    /// <summary>Gets the number of invalidation calls received from other instances.</summary>
    public long InvalidateCacheCalls => Interlocked.Read(ref _invalidateCacheCalls);

    /// <summary>Provides direct access to the L1 (in-memory) cache for advanced scenarios.</summary>
    public IInMemoryCache LocalCache { get; } = l1Cache;

    /// <summary>The auto-recovery queue, when <see cref="HybridCacheOptions.EnableAutoRecovery"/> is set.</summary>
    /// <remarks>
    /// The queue owns its own TimeProvider timer so it works for any HybridCache lifetime (default DI
    /// singleton, named keyed instances, or direct construction) and is torn down in DisposeAsync.
    /// </remarks>
    internal HybridCacheRecoveryQueue? RecoveryQueue { get; } =
        cacheOptions.EnableAutoRecovery
            ? new HybridCacheRecoveryQueue(
                cacheOptions,
                timeProvider ?? TimeProvider.System,
                logger ?? NullLogger<HybridCache>.Instance
            )
            : null;

    /// <summary>
    /// Handles incoming cache invalidation message from other instances.
    /// Called by <see cref="HybridCacheInvalidationConsumer"/>.
    /// </summary>
    internal async ValueTask HandleInvalidationAsync(CacheInvalidationMessage message, CancellationToken ct)
    {
        // Skip self-originated messages
        if (string.Equals(message.InstanceId, _instanceId, StringComparison.Ordinal))
        {
            return;
        }

        // Conflict check before applying the invalidation: queued recovery items older than this message lost
        // the race to another node; replaying them would resurrect stale data.
        RecoveryQueue?.OnIncomingInvalidation(message);

        _logger.LogInvalidatingLocalCacheFromRemote(
            message.InstanceId,
            message.Keys?.Length ?? (message.Key is not null ? 1 : 0),
            message.Prefix is not null,
            message.FlushAll
        );

        Interlocked.Increment(ref _invalidateCacheCalls);

        if (message.FlushAll)
        {
            _logger.LogFlushedLocalCache();
            // Seed the L2 provider's remove-generation marker from the origin timestamp FIRST, then wipe L1. The
            // order matters: if L1 were wiped first, a concurrent read in the window before the L2 marker is seeded
            // would miss the wiped L1 and fall through to L2 (marker not yet seeded), serving the stale entry. The
            // remove marker only invalidates entries older than the flush timestamp, so seeding it before the wipe
            // cannot hide a still-valid entry. Providers whose flush is physical implement SeedRemoveMarker as a
            // no-op (FusionCache's payload-carrying backplane — the timestamp travels with the notification).
            if (l2Cache is ISeedableTagMarkerCache l2Markers)
            {
                l2Markers.SeedRemoveMarker(message.Timestamp ?? _timeProvider.GetUtcNow());
            }

            // Physically wipe this receiver's L1 (drops its local fail-safe reserves, matching the originator).
            await LocalCache.FlushAsync(ct).ConfigureAwait(false);

            return;
        }

        if (message.Clear)
        {
            var clearAt = message.Timestamp ?? _timeProvider.GetUtcNow();

            // Seed the L1 clear-generation marker from the ORIGINATOR's timestamp (raise-only), not via ClearAsync
            // which would stamp the receiver's own clock: under cross-node clock skew a receiver lagging the origin
            // would write a marker older than a freshly-born local entry and fail to invalidate it. Reserves are
            // preserved (unlike FlushAll). Fall back to a local-clock ClearAsync only when L1 cannot be seeded.
            if (LocalCache is ISeedableTagMarkerCache l1Markers)
            {
                l1Markers.SeedClearMarker(clearAt);
            }
            else
            {
                await LocalCache.ClearAsync(ct).ConfigureAwait(false);
            }

            // Seed the L2 provider's process-local marker cache too (when it caches markers): without this, an
            // L1-miss read on this peer would fall through to L2 and observe the clear only after L2's refresh
            // window. The notification carries the timestamp, so no L2 round-trip is needed (FusionCache pattern).
            if (l2Cache is ISeedableTagMarkerCache l2Markers)
            {
                l2Markers.SeedClearMarker(clearAt);
            }

            return;
        }

        if (!string.IsNullOrEmpty(message.Prefix))
        {
            await LocalCache.RemoveByPrefixAsync(message.Prefix, ct).ConfigureAwait(false);
            return;
        }

        if (!string.IsNullOrEmpty(message.Tag))
        {
            var tagAt = message.Timestamp ?? _timeProvider.GetUtcNow();

            // Seed the L1 tag marker from the ORIGINATOR's timestamp (raise-only), not via RemoveByTagAsync which
            // stamps the receiver's local clock — under clock skew a lagging receiver would record a marker older
            // than the invalidated entries' CreatedAt and miss the invalidation. Family-2 logical invalidation is
            // version-pinned by entry birth time, so a pending recovery write that landed after this invalidation
            // carries a newer CreatedAt and is naturally not invalidated by the older marker. Fall back to a
            // local-clock RemoveByTagAsync only when L1 cannot be seeded.
            if (LocalCache is ISeedableTagMarkerCache l1Markers)
            {
                l1Markers.SeedTagMarker(message.Tag, tagAt);
            }
            else
            {
                await LocalCache.RemoveByTagAsync(message.Tag, ct).ConfigureAwait(false);
            }

            // Seed the L2 provider's process-local marker cache too so an L1-miss read on this peer observes the
            // invalidation immediately rather than after L2's refresh window. The notification carries the
            // timestamp, so no L2 round-trip is needed (FusionCache's payload-carrying-backplane optimization).
            if (l2Cache is ISeedableTagMarkerCache l2Markers)
            {
                l2Markers.SeedTagMarker(message.Tag, tagAt);
            }

            return;
        }

        if (message.Keys is { Length: > 0 })
        {
            var keys = message.Keys;

            if (RecoveryQueue is not null)
            {
                // A key with a surviving recovery item has local intent at least as new as this message (the
                // conflict pass above dropped everything older): wiping its L1 entry would discard the newer
                // local write and make the stamp-verified replay drop itself as obsolete.
                keys = Array.FindAll(keys, key => !_ShouldIgnoreInvalidationFor(key));
            }

            if (keys.Length > 0)
            {
                if (message.Expire)
                {
                    // Logical expiration preserves each peer's fail-safe reserve: expire per key rather than
                    // removing, since there is no bulk logical-expire on the local store.
                    foreach (var key in keys)
                    {
                        await LocalCache.ExpireAsync(key, ct).ConfigureAwait(false);
                    }
                }
                else
                {
                    await LocalCache.RemoveAllAsync(keys, ct).ConfigureAwait(false);
                }
            }

            return;
        }

        if (!string.IsNullOrEmpty(message.Key))
        {
            if (!_ShouldIgnoreInvalidationFor(message.Key))
            {
                if (message.Expire)
                {
                    await LocalCache.ExpireAsync(message.Key, ct).ConfigureAwait(false);
                }
                else
                {
                    await LocalCache.RemoveAsync(message.Key, ct).ConfigureAwait(false);
                }
            }

            return;
        }

        _logger.LogUnknownInvalidateCacheMessage();
    }

    #region ICache - Get Operations

    /// <inheritdoc />
    public async ValueTask<CacheValue<T>> GetOrAddAsync<T>(
        string key,
        Func<CancellationToken, ValueTask<T?>> factory,
        CacheEntryOptions options,
        CancellationToken cancellationToken = default
    )
    {
        _ThrowIfDisposed();
        Argument.IsNotNullOrEmpty(key);
        Argument.IsNotNull(factory);

        cancellationToken.ThrowIfCancellationRequested();

        return await _coordinator.GetOrAddAsync(this, key, factory, options, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async ValueTask<CacheValue<T>> GetOrAddAsync<T>(
        string key,
        Func<CacheFactoryContext<T>, CancellationToken, ValueTask<CacheFactoryResult<T>>> factory,
        CacheEntryOptions options,
        CancellationToken cancellationToken = default
    )
    {
        _ThrowIfDisposed();
        Argument.IsNotNullOrEmpty(key);
        Argument.IsNotNull(factory);

        cancellationToken.ThrowIfCancellationRequested();

        return await _coordinator.GetOrAddAsync(this, key, factory, options, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async ValueTask RefreshAsync(string key, CancellationToken cancellationToken = default)
    {
        _ThrowIfDisposed();
        Argument.IsNotNullOrEmpty(key);
        cancellationToken.ThrowIfCancellationRequested();

        if (_IsDistributedCacheCircuitClosed())
        {
            try
            {
                await l2Cache.RefreshAsync(key, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception exception)
                when (!FactoryCacheCoordinator.IsCallerCancellation(exception, cancellationToken))
            {
                _OpenDistributedCacheCircuit(exception, key);
                _logger.LogFailedToRefreshL2Cache(exception, key);
            }
        }

        await LocalCache.RefreshAsync(key, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async ValueTask<CacheValue<T>> GetAsync<T>(string key, CancellationToken cancellationToken = default)
    {
        _ThrowIfDisposed();
        Argument.IsNotNullOrEmpty(key);
        cancellationToken.ThrowIfCancellationRequested();

        var cacheValue = await LocalCache.GetAsync<T>(key, cancellationToken).ConfigureAwait(false);
        if (cacheValue.HasValue)
        {
            _logger.LogLocalCacheHit(key);
            Interlocked.Increment(ref _localCacheHits);
            return cacheValue;
        }

        _logger.LogLocalCacheMiss(key);
        var l2Read = await _ReadFromL2Async(
                key,
                ct => l2Cache.GetWithExpirationAsync<T>(key, ct),
                _SelectDistributedReadTimeout(hasLocalFallback: false, softCanDegradeToMiss: true),
                DistributedCacheTimeoutKind.Soft,
                cancellationToken
            )
            .ConfigureAwait(false);

        if (!l2Read.IsSuccess)
        {
            if (l2Read.Exception is { } exception)
            {
                _logger.LogFailedToReadFromL2Cache(exception, key);
            }

            return CacheValue<T>.NoValue;
        }

        cacheValue = l2Read.Value.Value;

        if (cacheValue.HasValue)
        {
            var localExpiration = _GetLocalExpiration(l2Read.Value.Expiration);
            _logger.LogSettingLocalCacheKey(key, localExpiration);
            await LocalCache
                .UpsertAsync(key, cacheValue.Value, localExpiration, cancellationToken)
                .ConfigureAwait(false);
        }

        return cacheValue;
    }

    /// <inheritdoc />
    public async ValueTask<IDictionary<string, CacheValue<T>>> GetAllAsync<T>(
        IEnumerable<string> cacheKeys,
        CancellationToken cancellationToken = default
    )
    {
        _ThrowIfDisposed();
        Argument.IsNotNull(cacheKeys);
        cancellationToken.ThrowIfCancellationRequested();

        var keysCollection = cacheKeys.AsICollection();
        if (keysCollection.Count == 0)
        {
            return new Dictionary<string, CacheValue<T>>(StringComparer.Ordinal);
        }

        var localValues = await LocalCache.GetAllAsync<T>(keysCollection, cancellationToken).ConfigureAwait(false);

        // Collect keys that weren't found in local cache
        var missedKeys = new List<string>(keysCollection.Count);
        foreach (var kvp in localValues)
        {
            if (kvp.Value.HasValue)
            {
                _logger.LogLocalCacheHit(kvp.Key);
            }
            else
            {
                _logger.LogLocalCacheMiss(kvp.Key);
                missedKeys.Add(kvp.Key);
            }
        }

        Interlocked.Add(ref _localCacheHits, keysCollection.Count - missedKeys.Count);

        // All keys found in local cache
        if (missedKeys.Count == 0)
        {
            return localValues;
        }

        var result = new Dictionary<string, CacheValue<T>>(localValues, StringComparer.Ordinal);
        var distributedRead = await _ReadFromL2Async(
                // Diagnostic-only label: _ReadFromL2Async uses this key solely for timeout/circuit log fields, not
                // for the read itself (the read is the delegate below). A synthetic bulk marker keeps the logs from
                // looking single-key.
                $"[bulk:{missedKeys.Count}]",
                ct => l2Cache.GetAllWithExpirationAsync<T>(missedKeys, ct),
                _SelectDistributedReadTimeout(hasLocalFallback: false, softCanDegradeToMiss: true),
                DistributedCacheTimeoutKind.Soft,
                cancellationToken
            )
            .ConfigureAwait(false);

        if (!distributedRead.IsSuccess)
        {
            // Include a small key sample so operators can identify the affected keys without a single-key flood.
            var keySample = string.Join(", ", missedKeys.Take(5));

            if (distributedRead.Exception is { } exception)
            {
                // Degrade to the partial L1 result, mirroring the single-key GetAsync contract:
                // an L2 read fault is logged then swallowed so callers always get a best-effort response.
                _logger.LogFailedBulkL2CacheOperationWithSample(exception, missedKeys.Count, keySample);
            }
            else
            {
                // Timeout or circuit-open: same degrade contract, but no exception to attach to the log entry.
                _logger.LogBulkDistributedCacheReadDegradedWithSample(
                    missedKeys.Count,
                    distributedRead.Status.ToString(),
                    keySample
                );
            }

            return result;
        }

        var distributedResults = distributedRead.Value!;

        // Mirror each L2 hit into L1 capped by its exact remaining L2 logical expiration (so the L1 copy never
        // outlives L2 freshness). The enriched bulk read returns value + expiration in one call — no separate
        // per-key expiration round-trips needed. Collect the hits first, then fan the L1 upserts out in parallel
        // (each carries its own capped expiration, so a single shared-expiration batch call cannot be used).
        List<Task>? localUpserts = null;

        foreach (var kvp in distributedResults)
        {
            result[kvp.Key] = kvp.Value.Value;

            if (kvp.Value.Value is { HasValue: true, Value: not null })
            {
                var localExpiration = _GetLocalExpiration(kvp.Value.Expiration);
                _logger.LogSettingLocalCacheKey(kvp.Key, localExpiration);
                (localUpserts ??= []).Add(
                    LocalCache.UpsertAsync(kvp.Key, kvp.Value.Value.Value, localExpiration, cancellationToken).AsTask()
                );
            }
        }

        if (localUpserts is not null)
        {
            await Task.WhenAll(localUpserts).ConfigureAwait(false);
        }

        return result;
    }

    /// <inheritdoc />
    public async ValueTask<IDictionary<string, CacheValue<T>>> GetByPrefixAsync<T>(
        string prefix,
        CancellationToken cancellationToken = default
    )
    {
        _ThrowIfDisposed();
        Argument.IsNotNull(prefix);
        cancellationToken.ThrowIfCancellationRequested();

        // For prefix queries, go directly to L2 as L1 may not have all matching keys
        return await l2Cache.GetByPrefixAsync<T>(prefix, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async ValueTask<IReadOnlyList<string>> GetAllKeysByPrefixAsync(
        string prefix,
        CancellationToken cancellationToken = default
    )
    {
        _ThrowIfDisposed();
        Argument.IsNotNull(prefix);
        cancellationToken.ThrowIfCancellationRequested();

        return await l2Cache.GetAllKeysByPrefixAsync(prefix, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async ValueTask<long> GetCountAsync(string prefix = "", CancellationToken cancellationToken = default)
    {
        _ThrowIfDisposed();
        cancellationToken.ThrowIfCancellationRequested();

        return await l2Cache.GetCountAsync(prefix, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async ValueTask<bool> ExistsAsync(string key, CancellationToken cancellationToken = default)
    {
        _ThrowIfDisposed();
        Argument.IsNotNullOrEmpty(key);
        cancellationToken.ThrowIfCancellationRequested();

        // Check local cache first
        var localExists = await LocalCache.ExistsAsync(key, cancellationToken).ConfigureAwait(false);
        if (localExists)
        {
            _logger.LogLocalCacheHit(key);
            Interlocked.Increment(ref _localCacheHits);
            return true;
        }

        _logger.LogLocalCacheMiss(key);
        var l2Read = await _ReadFromL2Async(
                key,
                ct => l2Cache.ExistsAsync(key, ct),
                _SelectDistributedReadTimeout(hasLocalFallback: false, softCanDegradeToMiss: true),
                DistributedCacheTimeoutKind.Soft,
                cancellationToken
            )
            .ConfigureAwait(false);

        if (!l2Read.IsSuccess)
        {
            if (l2Read.Exception is { } exception)
            {
                _logger.LogFailedToReadFromL2Cache(exception, key);
            }

            return false;
        }

        return l2Read.Value;
    }

    /// <inheritdoc />
    public async ValueTask<TimeSpan?> GetExpirationAsync(string key, CancellationToken cancellationToken = default)
    {
        _ThrowIfDisposed();
        Argument.IsNotNullOrEmpty(key);
        cancellationToken.ThrowIfCancellationRequested();

        // Single L1 lookup: a null return means the key is absent, avoiding a separate ExistsAsync round-trip.
        var localExpiration = await LocalCache.GetExpirationAsync(key, cancellationToken).ConfigureAwait(false);
        if (localExpiration is not null)
        {
            _logger.LogLocalCacheHit(key);
            Interlocked.Increment(ref _localCacheHits);
            return localExpiration;
        }

        _logger.LogLocalCacheMiss(key);
        var l2Read = await _ReadFromL2Async(
                key,
                ct => l2Cache.GetExpirationAsync(key, ct),
                _SelectDistributedReadTimeout(hasLocalFallback: false, softCanDegradeToMiss: true),
                DistributedCacheTimeoutKind.Soft,
                cancellationToken
            )
            .ConfigureAwait(false);

        if (!l2Read.IsSuccess)
        {
            if (l2Read.Exception is { } exception)
            {
                _logger.LogFailedToReadFromL2Cache(exception, key);
            }

            return null;
        }

        return l2Read.Value;
    }

    /// <inheritdoc />
    public async ValueTask<CacheValue<ICollection<T>>> GetSetAsync<T>(
        string key,
        int? pageIndex = null,
        int pageSize = 100,
        CancellationToken cancellationToken = default
    )
    {
        _ThrowIfDisposed();
        Argument.IsNotNullOrEmpty(key);
        cancellationToken.ThrowIfCancellationRequested();

        var cacheValue = await LocalCache
            .GetSetAsync<T>(key, pageIndex, pageSize, cancellationToken)
            .ConfigureAwait(false);
        if (cacheValue.HasValue)
        {
            _logger.LogLocalCacheHit(key);
            Interlocked.Increment(ref _localCacheHits);
            return cacheValue;
        }

        _logger.LogLocalCacheMiss(key);
        var l2Read = await _ReadFromL2Async(
                key,
                async ct =>
                {
                    var value = await l2Cache.GetSetAsync<T>(key, pageIndex, pageSize, ct).ConfigureAwait(false);

                    if (!value.HasValue)
                    {
                        return new CacheValueWithExpiration<ICollection<T>>(value, null);
                    }

                    var expiration = await l2Cache.GetExpirationAsync(key, ct).ConfigureAwait(false);
                    return new CacheValueWithExpiration<ICollection<T>>(value, expiration);
                },
                _SelectDistributedReadTimeout(hasLocalFallback: false, softCanDegradeToMiss: true),
                DistributedCacheTimeoutKind.Soft,
                cancellationToken
            )
            .ConfigureAwait(false);

        if (!l2Read.IsSuccess)
        {
            if (l2Read.Exception is { } exception)
            {
                _logger.LogFailedToReadFromL2Cache(exception, key);
            }

            return CacheValue<ICollection<T>>.NoValue;
        }

        cacheValue = l2Read.Value.Value;

        if (cacheValue.HasValue)
        {
            var localExpiration = _GetLocalExpiration(l2Read.Value.Expiration);
            _logger.LogSettingLocalCacheKey(key, localExpiration);
            // Use UpsertAsync to replace any existing L1 data (not SetAddAsync which would merge)
            await LocalCache
                .UpsertAsync(key, cacheValue.Value, localExpiration, cancellationToken)
                .ConfigureAwait(false);
        }

        return cacheValue;
    }

    #endregion

    #region Private Helpers

    private async ValueTask _PublishInvalidationAsync(
        CacheInvalidationMessage message,
        CancellationToken ct,
        bool queueOnFailure = false
    )
    {
        // Stamp the publish time so receivers can run the auto-recovery conflict check against it. Replayed
        // invalidations arrive pre-stamped with the original write time so receivers still order them correctly
        // against operations that happened between the original write and its replay.
        if (message.Timestamp is null)
        {
            message = message with { Timestamp = _timeProvider.GetUtcNow() };
        }

        if (!string.Equals(message.CacheName, _cacheName, StringComparison.Ordinal))
        {
            message = message with { CacheName = _cacheName };
        }

        try
        {
            await publisher.PublishAsync(message, cancellationToken: ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (!FactoryCacheCoordinator.IsCallerCancellation(ex, ct))
        {
            // Publish failure is non-fatal: other instances may have stale L1 data
            // until their TTL expires. This is acceptable for eventual consistency.
            _logger.LogFailedToPublishCacheInvalidation(
                ex,
                message.Keys?.Length ?? (message.Key is not null ? 1 : 0),
                message.Prefix is not null,
                message.FlushAll
            );

            // Only single-key invalidations from the auto-recovery capture paths are queued for re-publish;
            // every other path keeps today's fire-and-forget behavior.
            if (queueOnFailure && RecoveryQueue is not null && message.Key is not null)
            {
                _QueuePublishRecovery(message);
            }

            // Fail loud after the log + recovery queueing: self-healing still happens (the queued item replays),
            // but the caller is also informed of the backplane outage. On detached background publish paths this
            // re-throw is observed and logged by the fire-and-forget fault net rather than surfacing to a caller.
            if (cacheOptions.ReThrowBackplaneExceptions)
            {
                throw;
            }
        }
    }

    private bool _ShouldIgnoreInvalidationFor(string key)
    {
        // Only consulted after the OnIncomingInvalidation conflict pass dropped every queued item older than
        // the message, so a surviving item means our pending local operation is at least as new: applying the
        // older foreign invalidation would wipe the newer local state it is about to replay.
        if (RecoveryQueue?.Contains(key) != true)
        {
            return false;
        }

        _logger.LogIgnoredStaleRemoteInvalidation(key);
        return true;
    }

    /// <summary>
    /// Detaches the supplied background L2 work as fire-and-forget and attaches an observe-faulted continuation
    /// so a fault can never become an unobserved task exception (host-crash safe on .NET's escalation policy).
    /// The work itself is expected to handle its own L2/publish failures (recovery routing or best-effort log);
    /// this continuation is the last-resort net for anything that still escapes — mirrors the coordinator's
    /// <c>_ObserveFaultedTask</c> pattern. The lambda must capture every value it needs (key, value, message)
    /// BEFORE calling this so it never touches disposal-racing state after the caller returns.
    /// </summary>
    private void _RunDetached(Func<Task> work, string key)
    {
        Task task;

        try
        {
            task = work();
        }
        catch (Exception exception)
        {
            // A synchronous throw before the first await still must not surface to the (already-returned) caller.
            _logger.LogBackgroundDistributedCacheOperationFailed(exception, key, exception.GetType().Name);
            return;
        }

        if (task.IsCompletedSuccessfully)
        {
            return;
        }

        _ = task.ContinueWith(
            (faulted, state) =>
            {
                var (logger, faultedKey) = ((ILogger, string))state!;
                logger.LogBackgroundDistributedCacheOperationFailed(
                    faulted.Exception!,
                    faultedKey,
                    faulted.Exception!.GetType().Name
                );
            },
            (_logger, key),
            CancellationToken.None,
            TaskContinuationOptions.OnlyOnFaulted,
            TaskScheduler.Default
        );
    }

    private void _ThrowIfDisposed()
    {
        Ensure.NotDisposed(Volatile.Read(ref _isDisposed) == 1, this);
    }

    private DateTime _GetUtcNow() => _timeProvider.GetUtcNow().UtcDateTime;

    private static DateTime _Min(DateTime left, DateTime right) => left <= right ? left : right;

    private TimeSpan? _GetLocalExpiration(TimeSpan? expiration)
    {
        if (!cacheOptions.DefaultLocalExpiration.HasValue)
        {
            return expiration;
        }

        return expiration < cacheOptions.DefaultLocalExpiration.Value
            ? expiration
            : cacheOptions.DefaultLocalExpiration;
    }

    #endregion

    #region IAsyncDisposable

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _isDisposed, 1) == 1)
        {
            return;
        }

        _coordinator.Dispose();

        if (RecoveryQueue is not null)
        {
            // Cancel the timer before draining so no new ProcessAsync pass starts after we await the active one.
            RecoveryQueue.Dispose();
            await RecoveryQueue.DrainAsync(CancellationToken.None).ConfigureAwait(false);
        }
    }

    #endregion
}

internal static partial class HybridCacheLoggerExtensions
{
    [LoggerMessage(
        EventId = 21,
        EventName = "FailedBulkL2CacheOperationWithSample",
        Level = LogLevel.Warning,
        Message = "Failed to perform a bulk L2 cache operation for {KeyCount} key(s); degrading to partial L1 result (sample: {KeySample})"
    )]
    public static partial void LogFailedBulkL2CacheOperationWithSample(
        this ILogger logger,
        Exception exception,
        int keyCount,
        string keySample
    );

    [LoggerMessage(
        EventId = 22,
        EventName = "BulkDistributedCacheReadDegradedWithSample",
        Level = LogLevel.Warning,
        Message = "Bulk L2 cache read for {KeyCount} key(s) did not complete ({Reason}); degrading to partial L1 result (sample: {KeySample})"
    )]
    public static partial void LogBulkDistributedCacheReadDegradedWithSample(
        this ILogger logger,
        int keyCount,
        string reason,
        string keySample
    );
}
