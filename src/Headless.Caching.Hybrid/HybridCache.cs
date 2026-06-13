// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;
using Headless.Messaging;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Headless.Caching;

/// <summary>
/// Two-tier hybrid cache combining fast in-memory L1 cache with distributed L2 cache.
/// Provides automatic cross-instance cache invalidation via messaging.
/// </summary>
/// <remarks>
/// <para><b>Read path:</b></para>
/// <list type="number">
/// <item>Check L1 (local in-memory) - fastest, per-instance</item>
/// <item>Check L2 (distributed) - slower but shared across instances</item>
/// <item>Execute factory if both miss, populate both caches, publish invalidation so peers drop stale L1</item>
/// </list>
/// <para><b>Write/Invalidation path:</b></para>
/// <list type="number">
/// <item>Publish invalidation message first (to minimize race window)</item>
/// <item>Update/remove from L1 and L2</item>
/// <item>Other instances receive message and invalidate their L1</item>
/// </list>
/// </remarks>
[PublicAPI]
public sealed partial class HybridCache(
    IInMemoryCache l1Cache,
    IRemoteCache l2Cache,
    IBus publisher,
    HybridCacheOptions options,
    ILogger<HybridCache>? logger = null,
    TimeProvider? timeProvider = null,
    ICacheFactoryLockProvider? factoryLockProvider = null
) : ICache, IFactoryCacheStore, IAsyncDisposable
{
    private readonly ILogger _logger = logger ?? NullLogger<HybridCache>.Instance;
    private readonly TimeProvider _timeProvider = timeProvider ?? TimeProvider.System;
    private readonly string _instanceId = options.InstanceId ?? Guid.NewGuid().ToString("N");
    private readonly string? _cacheName = options.CacheName;
    private readonly FactoryCacheCoordinator _coordinator = new(
        timeProvider ?? TimeProvider.System,
        logger,
        factoryLockProvider
    );

    private long _localCacheHits;
    private long _invalidateCacheCalls;
    private int _isDisposed;

    /// <inheritdoc />
    public CacheEntryOptions? DefaultEntryOptions { get; } = options.DefaultEntryOptions;

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
        options.EnableAutoRecovery
            ? new HybridCacheRecoveryQueue(
                options,
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
            await LocalCache.FlushAsync(ct).ConfigureAwait(false);
            return;
        }

        if (!string.IsNullOrEmpty(message.Prefix))
        {
            await LocalCache.RemoveByPrefixAsync(message.Prefix, ct).ConfigureAwait(false);
            return;
        }

        if (!string.IsNullOrEmpty(message.Tag))
        {
            // Bulk-remove is safe when there are no pending recovery items to protect: auto-recovery is off, or
            // its queue is currently empty. Reading Count avoids the GetTaggedKeys snapshot + per-key walk on the
            // common (no-outage) path.
            if (RecoveryQueue is null || RecoveryQueue.Count == 0)
            {
                await LocalCache.RemoveByTagAsync(message.Tag, ct).ConfigureAwait(false);
                return;
            }

            // Recovery-aware tag invalidation: a key whose pending recovery item is NEWER than this message won
            // the race — its L1 entry holds the most-recent local intent and must not be wiped (the replay would
            // find nothing, declare itself obsolete, and the value would be lost on every node). Go per-key over a
            // snapshot of the tag members (never bulk): a key tagged after the snapshot is simply absent from it,
            // so its newer value is left untouched; keys with a surviving newer pending item are skipped, the rest
            // removed. GetTaggedKeys returns the cache's user-facing (unprefixed) keys, matching both the recovery
            // queue's keys and RemoveAsync's own prefixing.
            var taggedKeys = LocalCache.GetTaggedKeys(message.Tag);

            foreach (var key in taggedKeys)
            {
                if (RecoveryQueue.HasNewerPendingItemThan(key, message.Timestamp))
                {
                    _logger.LogIgnoredStaleRemoteInvalidation(key);
                }
                else
                {
                    await LocalCache.RemoveAsync(key, ct).ConfigureAwait(false);
                }
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

        var keysCollection = cacheKeys as ICollection<string> ?? cacheKeys.ToList();
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
                missedKeys[0],
                ct => l2Cache.GetAllWithExpirationAsync<T>(missedKeys, ct),
                _SelectDistributedReadTimeout(hasLocalFallback: false, softCanDegradeToMiss: true),
                DistributedCacheTimeoutKind.Soft,
                cancellationToken
            )
            .ConfigureAwait(false);

        if (!distributedRead.IsSuccess)
        {
            if (distributedRead.Exception is { } exception)
            {
                _logger.LogFailedBulkL2CacheOperation(exception, missedKeys.Count);
                System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(exception).Throw();
            }

            return result;
        }

        var distributedResults = distributedRead.Value!;

        // Mirror each L2 hit into L1 capped by its exact remaining L2 logical expiration (so the L1 copy never
        // outlives L2 freshness). The enriched bulk read returns value + expiration in one call — no separate
        // per-key expiration round-trips needed.
        foreach (var kvp in distributedResults)
        {
            result[kvp.Key] = kvp.Value.Value;

            if (kvp.Value.Value is { HasValue: true, Value: not null })
            {
                var localExpiration = _GetLocalExpiration(kvp.Value.Expiration);
                _logger.LogSettingLocalCacheKey(kvp.Key, localExpiration);
                await LocalCache
                    .UpsertAsync(kvp.Key, kvp.Value.Value.Value, localExpiration, cancellationToken)
                    .ConfigureAwait(false);
            }
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

        // Check if key exists in local cache first
        var localExists = await LocalCache.ExistsAsync(key, cancellationToken).ConfigureAwait(false);
        if (localExists)
        {
            _logger.LogLocalCacheHit(key);
            Interlocked.Increment(ref _localCacheHits);
            return await LocalCache.GetExpirationAsync(key, cancellationToken).ConfigureAwait(false);
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
                .UpsertAsync(key, cacheValue.Value!, localExpiration, cancellationToken)
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
        if (!options.DefaultLocalExpiration.HasValue)
        {
            return expiration;
        }

        return expiration.HasValue && expiration.Value < options.DefaultLocalExpiration.Value
            ? expiration
            : options.DefaultLocalExpiration;
    }

    #endregion

    #region IAsyncDisposable

    /// <inheritdoc />
    public ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _isDisposed, 1) == 1)
        {
            return ValueTask.CompletedTask;
        }

        _coordinator.Dispose();
        RecoveryQueue?.Dispose();
        return ValueTask.CompletedTask;
    }

    #endregion
}
