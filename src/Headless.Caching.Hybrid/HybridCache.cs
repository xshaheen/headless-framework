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
            await LocalCache.RemoveByTagAsync(message.Tag, ct).ConfigureAwait(false);
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
                async ct =>
                {
                    var value = await l2Cache.GetAsync<T>(key, ct).ConfigureAwait(false);
                    var expiration = value.HasValue
                        ? await l2Cache.GetExpirationAsync(key, ct).ConfigureAwait(false)
                        : null;

                    return (Value: value, Expiration: expiration);
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
                ct => l2Cache.GetAllAsync<T>(missedKeys, ct),
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
        // outlives L2 freshness). The bulk L2 read returns values only, so fetch the per-key expirations
        // concurrently in one WhenAll rather than serially per key.
        var hits = new List<KeyValuePair<string, CacheValue<T>>>(distributedResults.Count);
        foreach (var kvp in distributedResults)
        {
            result[kvp.Key] = kvp.Value;
            if (kvp.Value is { HasValue: true, Value: not null })
            {
                hits.Add(kvp);
            }
        }

        if (hits.Count == 0)
        {
            return result;
        }

        var expirationReads = await Task.WhenAll(
                hits.Select(hit =>
                    _ReadFromL2Async(
                            hit.Key,
                            ct => l2Cache.GetExpirationAsync(hit.Key, ct),
                            _SelectDistributedReadTimeout(hasLocalFallback: false, softCanDegradeToMiss: true),
                            DistributedCacheTimeoutKind.Soft,
                            cancellationToken
                        )
                        .AsTask()
                )
            )
            .ConfigureAwait(false);

        var valuesToCache = new Dictionary<string, (T Value, TimeSpan? Expiration)>(StringComparer.Ordinal);
        for (var i = 0; i < hits.Count; i++)
        {
            var expirationRead = expirationReads[i];
            if (expirationRead.IsSuccess)
            {
                valuesToCache[hits[i].Key] = (hits[i].Value.Value!, _GetLocalExpiration(expirationRead.Value));
            }
        }

        foreach (var (key, (value, localExpiration)) in valuesToCache)
        {
            _logger.LogSettingLocalCacheKey(key, localExpiration);
            await LocalCache.UpsertAsync(key, value, localExpiration, cancellationToken).ConfigureAwait(false);
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
                    var expiration = value.HasValue
                        ? await l2Cache.GetExpirationAsync(key, ct).ConfigureAwait(false)
                        : null;

                    return (Value: value, Expiration: expiration);
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

    #region ICache - Update Operations

    /// <inheritdoc />
    public async ValueTask<bool> UpsertAsync<T>(
        string key,
        T? value,
        TimeSpan? expiration,
        CancellationToken cancellationToken = default
    )
    {
        _ThrowIfDisposed();
        Argument.IsNotNullOrEmpty(key);
        cancellationToken.ThrowIfCancellationRequested();

        if (expiration is { Ticks: <= 0 })
        {
            await RemoveAsync(key, cancellationToken).ConfigureAwait(false);
            return false;
        }

        _logger.LogSettingKey(key, expiration);

        // Background path: write L1 synchronously and detach the L2 write + publish. The caller's result no
        // longer reflects the L2 response, so we optimistically populate L1 (the additive write succeeded
        // locally) and return true. Capture every value the detached lambda needs before returning so it never
        // races disposal. A failed background write routes to recovery (when enabled) or is logged and swallowed.
        if (options.AllowBackgroundDistributedCacheOperations)
        {
            var localExpiration = _GetLocalExpiration(expiration);
            await LocalCache.UpsertAsync(key, value, localExpiration, cancellationToken).ConfigureAwait(false);

            _RunDetached(() => _BackgroundScalarUpsertAsync(key, value, expiration), key);

            return true;
        }

        bool updated;

        if (!_IsDistributedCacheCircuitClosed())
        {
            updated = true;
        }
        else
        {
            try
            {
                updated = await l2Cache.UpsertAsync(key, value, expiration, cancellationToken).ConfigureAwait(false);
                RecoveryQueue?.OnSuccessfulL2Operation(key);
            }
            catch (Exception exception)
                when (!FactoryCacheCoordinator.IsCallerCancellation(exception, cancellationToken)
                    && (RecoveryQueue is not null || options.DistributedCacheCircuitBreakerDuration > TimeSpan.Zero)
                )
            {
                // Degraded mode: with recovery on, queue the L2 write; with only the circuit breaker on, avoid
                // amplifying an unhealthy L2 and let the caller succeed against L1 for this additive write.
                _OpenDistributedCacheCircuit(exception, key);
                _logger.LogFailedToWriteToL2Cache(exception, key);
                if (RecoveryQueue is not null)
                {
                    _QueueScalarUpsertRecovery(key, value, expiration);
                }

                updated = true;
            }
        }

        if (updated)
        {
            var localExpiration = _GetLocalExpiration(expiration);
            await LocalCache.UpsertAsync(key, value, localExpiration, cancellationToken).ConfigureAwait(false);
        }
        else
        {
            // Remove from local cache when upsert fails
            await LocalCache.RemoveAsync(key, cancellationToken).ConfigureAwait(false);
        }

        await _PublishInvalidationAsync(
                new CacheInvalidationMessage { InstanceId = _instanceId, Key = key },
                cancellationToken,
                queueOnFailure: true
            )
            .ConfigureAwait(false);

        return updated;
    }

    /// <summary>
    /// The detached L2 tail of a scalar <see cref="UpsertAsync{T}"/> when background distributed operations are
    /// enabled. Runs with <see cref="CancellationToken.None"/>: the caller's token is gone once it returned, and
    /// cancelling a fire-and-forget L2 write would only abandon it. On L2 failure it routes to auto-recovery when
    /// enabled, otherwise logs and swallows (best-effort — the caller already succeeded against L1).
    /// </summary>
    private async Task _BackgroundScalarUpsertAsync<T>(string key, T? value, TimeSpan? expiration)
    {
        if (!_IsDistributedCacheCircuitClosed())
        {
            if (RecoveryQueue is not null)
            {
                _QueueScalarUpsertRecovery(key, value, expiration);
            }
        }
        else
        {
            try
            {
                await l2Cache.UpsertAsync(key, value, expiration, CancellationToken.None).ConfigureAwait(false);
                RecoveryQueue?.OnSuccessfulL2Operation(key);
            }
            catch (Exception exception)
                when (!FactoryCacheCoordinator.IsCallerCancellation(exception, CancellationToken.None))
            {
                _OpenDistributedCacheCircuit(exception, key);
                _logger.LogFailedToWriteToL2Cache(exception, key);

                if (RecoveryQueue is not null)
                {
                    // Same capture the synchronous degraded path uses: queue the failed L2 write for replay.
                    _QueueScalarUpsertRecovery(key, value, expiration);
                }

                // Auto-recovery off: best-effort, swallow. The caller already returned success (fire-and-forget).
            }
        }

        await _PublishInvalidationAsync(
                new CacheInvalidationMessage { InstanceId = _instanceId, Key = key },
                CancellationToken.None,
                queueOnFailure: true
            )
            .ConfigureAwait(false);
    }

    /// <summary>
    /// The detached L2 tail of a bulk <see cref="UpsertAllAsync{T}"/> when background distributed operations are
    /// enabled. Bulk ops are not captured by auto-recovery (issue #440), so an L2 failure here is best-effort:
    /// logged and swallowed. The publish runs regardless so peers still drop their stale L1 entries.
    /// </summary>
    private async Task _BackgroundBulkUpsertAsync<T>(
        Dictionary<string, T> snapshot,
        string[] keys,
        TimeSpan? expiration
    )
    {
        if (_IsDistributedCacheCircuitClosed())
        {
            try
            {
                await l2Cache.UpsertAllAsync(snapshot, expiration, CancellationToken.None).ConfigureAwait(false);
            }
            catch (Exception exception)
                when (!FactoryCacheCoordinator.IsCallerCancellation(exception, CancellationToken.None))
            {
                if (keys.Length > 0)
                {
                    _OpenDistributedCacheCircuit(exception, keys[0]);
                }

                _logger.LogFailedBulkL2CacheOperation(exception, snapshot.Count);
            }
        }

        await _PublishInvalidationAsync(
                new CacheInvalidationMessage { InstanceId = _instanceId, Keys = keys },
                CancellationToken.None
            )
            .ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async ValueTask<bool> UpsertEntryAsync<T>(
        string key,
        T? value,
        CacheEntryOptions options,
        CancellationToken cancellationToken = default
    )
    {
        _ThrowIfDisposed();
        Argument.IsNotNullOrEmpty(key);
        cancellationToken.ThrowIfCancellationRequested();

        // The hybrid store write fans out to L2 then L1 with the full per-entry metadata (tags included) and
        // publishes the key invalidation itself: every value-write through the composite store broadcasts.
        await ((IFactoryCacheStore)this)
            .UpsertEntryAsync(key, value, options, _timeProvider, cancellationToken)
            .ConfigureAwait(false);

        return true;
    }

    /// <inheritdoc />
    public async ValueTask<int> UpsertAllAsync<T>(
        IDictionary<string, T> value,
        TimeSpan? expiration,
        CancellationToken cancellationToken = default
    )
    {
        _ThrowIfDisposed();
        Argument.IsNotNull(value);
        cancellationToken.ThrowIfCancellationRequested();

        if (value.Count == 0)
        {
            return 0;
        }

        if (expiration is { Ticks: <= 0 })
        {
            await RemoveAllAsync(value.Keys, cancellationToken).ConfigureAwait(false);
            return 0;
        }

        _logger.LogSettingKeys(value.Keys, expiration);

        // Background path: write L1 synchronously and detach the L2 bulk write + publish. The caller no longer
        // depends on the L2 outcome, so we optimistically populate L1 with every entry and report the full count.
        // Bulk ops are not captured by auto-recovery (issue #440), so a failed background bulk write is purely
        // best-effort: logged and swallowed. Snapshot the dictionary and key array before detaching so the
        // background lambda owns immutable state and never observes a caller-side mutation.
        if (options.AllowBackgroundDistributedCacheOperations)
        {
            var snapshot = new Dictionary<string, T>(value, StringComparer.Ordinal);
            var keys = snapshot.Keys.ToArray();
            var localExpiration = _GetLocalExpiration(expiration);
            await LocalCache.UpsertAllAsync(snapshot, localExpiration, cancellationToken).ConfigureAwait(false);

            _RunDetached(() => _BackgroundBulkUpsertAsync(snapshot, keys, expiration), keys.Length > 0 ? keys[0] : "");

            return snapshot.Count;
        }

        int setCount;

        if (!_IsDistributedCacheCircuitClosed())
        {
            setCount = value.Count;
        }
        else
        {
            try
            {
                setCount = await l2Cache.UpsertAllAsync(value, expiration, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception exception)
                when (!FactoryCacheCoordinator.IsCallerCancellation(exception, cancellationToken)
                    && options.DistributedCacheCircuitBreakerDuration > TimeSpan.Zero
                )
            {
                _OpenDistributedCacheCircuit(exception, value.Keys.First());
                _logger.LogFailedBulkL2CacheOperation(exception, value.Count);
                setCount = value.Count;
            }
        }

        if (setCount == value.Count)
        {
            var localExpiration = _GetLocalExpiration(expiration);
            await LocalCache.UpsertAllAsync(value, localExpiration, cancellationToken).ConfigureAwait(false);
        }
        else
        {
            // Remove all keys from local cache when set fails or partially succeeds
            await LocalCache.RemoveAllAsync(value.Keys, cancellationToken).ConfigureAwait(false);
        }

        await _PublishInvalidationAsync(
                new CacheInvalidationMessage { InstanceId = _instanceId, Keys = value.Keys.ToArray() },
                cancellationToken
            )
            .ConfigureAwait(false);

        return setCount;
    }

    /// <inheritdoc />
    public async ValueTask<bool> TryInsertAsync<T>(
        string key,
        T? value,
        TimeSpan? expiration,
        CancellationToken cancellationToken = default
    )
    {
        _ThrowIfDisposed();
        Argument.IsNotNullOrEmpty(key);
        cancellationToken.ThrowIfCancellationRequested();

        if (expiration is { Ticks: <= 0 })
        {
            await RemoveAsync(key, cancellationToken).ConfigureAwait(false);
            return false;
        }

        _logger.LogAddingKeyToLocalCache(key, expiration);

        var added = await l2Cache.TryInsertAsync(key, value, expiration, cancellationToken).ConfigureAwait(false);

        if (added)
        {
            var localExpiration = _GetLocalExpiration(expiration);
            await LocalCache.UpsertAsync(key, value, localExpiration, cancellationToken).ConfigureAwait(false);
        }

        return added;
    }

    /// <inheritdoc />
    public async ValueTask<bool> TryReplaceAsync<T>(
        string key,
        T? value,
        TimeSpan? expiration,
        CancellationToken cancellationToken = default
    )
    {
        _ThrowIfDisposed();
        Argument.IsNotNullOrEmpty(key);
        cancellationToken.ThrowIfCancellationRequested();

        if (expiration is { Ticks: <= 0 })
        {
            await RemoveAsync(key, cancellationToken).ConfigureAwait(false);
            return false;
        }

        var replaced = await l2Cache.TryReplaceAsync(key, value, expiration, cancellationToken).ConfigureAwait(false);

        if (replaced)
        {
            var localExpiration = _GetLocalExpiration(expiration);
            await LocalCache.UpsertAsync(key, value, localExpiration, cancellationToken).ConfigureAwait(false);
        }
        else
        {
            // Remove from local cache when replace fails
            await LocalCache.RemoveAsync(key, cancellationToken).ConfigureAwait(false);
        }

        await _PublishInvalidationAsync(
                new CacheInvalidationMessage { InstanceId = _instanceId, Key = key },
                cancellationToken
            )
            .ConfigureAwait(false);

        return replaced;
    }

    /// <inheritdoc />
    public async ValueTask<bool> TryReplaceIfEqualAsync<T>(
        string key,
        T? expected,
        T? value,
        TimeSpan? expiration,
        CancellationToken cancellationToken = default
    )
    {
        _ThrowIfDisposed();
        Argument.IsNotNullOrEmpty(key);
        cancellationToken.ThrowIfCancellationRequested();

        if (expiration is { Ticks: <= 0 })
        {
            await RemoveAsync(key, cancellationToken).ConfigureAwait(false);
            return false;
        }

        var replaced = await l2Cache
            .TryReplaceIfEqualAsync(key, expected, value, expiration, cancellationToken)
            .ConfigureAwait(false);

        if (replaced)
        {
            // Use UpsertAsync instead of TryReplaceIfEqualAsync for local cache because we know
            // the distributed cache now has this exact value, and we need local cache to be in sync
            var localExpiration = _GetLocalExpiration(expiration);
            await LocalCache.UpsertAsync(key, value, localExpiration, cancellationToken).ConfigureAwait(false);
        }
        else
        {
            // Remove from local cache when replace fails
            await LocalCache.RemoveAsync(key, cancellationToken).ConfigureAwait(false);
        }

        await _PublishInvalidationAsync(
                new CacheInvalidationMessage { InstanceId = _instanceId, Key = key },
                cancellationToken
            )
            .ConfigureAwait(false);

        return replaced;
    }

    /// <inheritdoc />
    public async ValueTask<double> IncrementAsync(
        string key,
        double amount,
        TimeSpan? expiration,
        CancellationToken cancellationToken = default
    )
    {
        _ThrowIfDisposed();
        Argument.IsNotNullOrEmpty(key);
        cancellationToken.ThrowIfCancellationRequested();

        if (expiration is { Ticks: <= 0 })
        {
            await RemoveAsync(key, cancellationToken).ConfigureAwait(false);
            return 0;
        }

        var newValue = await l2Cache.IncrementAsync(key, amount, expiration, cancellationToken).ConfigureAwait(false);

        if (newValue == 0)
        {
            // When result is 0, remove from local cache (conservative approach)
            await LocalCache.RemoveAsync(key, cancellationToken).ConfigureAwait(false);
        }
        else
        {
            var localExpiration = _GetLocalExpiration(expiration);
            await LocalCache.UpsertAsync(key, newValue, localExpiration, cancellationToken).ConfigureAwait(false);
        }

        await _PublishInvalidationAsync(
                new CacheInvalidationMessage { InstanceId = _instanceId, Key = key },
                cancellationToken
            )
            .ConfigureAwait(false);

        return newValue;
    }

    /// <inheritdoc />
    public async ValueTask<long> IncrementAsync(
        string key,
        long amount,
        TimeSpan? expiration,
        CancellationToken cancellationToken = default
    )
    {
        _ThrowIfDisposed();
        Argument.IsNotNullOrEmpty(key);
        cancellationToken.ThrowIfCancellationRequested();

        if (expiration is { Ticks: <= 0 })
        {
            await RemoveAsync(key, cancellationToken).ConfigureAwait(false);
            return 0;
        }

        var newValue = await l2Cache.IncrementAsync(key, amount, expiration, cancellationToken).ConfigureAwait(false);

        if (newValue == 0)
        {
            // When result is 0, remove from local cache (conservative approach)
            await LocalCache.RemoveAsync(key, cancellationToken).ConfigureAwait(false);
        }
        else
        {
            var localExpiration = _GetLocalExpiration(expiration);
            await LocalCache.UpsertAsync(key, newValue, localExpiration, cancellationToken).ConfigureAwait(false);
        }

        await _PublishInvalidationAsync(
                new CacheInvalidationMessage { InstanceId = _instanceId, Key = key },
                cancellationToken
            )
            .ConfigureAwait(false);

        return newValue;
    }

    /// <inheritdoc />
    public async ValueTask<double> SetIfHigherAsync(
        string key,
        double value,
        TimeSpan? expiration,
        CancellationToken cancellationToken = default
    )
    {
        _ThrowIfDisposed();
        Argument.IsNotNullOrEmpty(key);
        cancellationToken.ThrowIfCancellationRequested();

        if (expiration is { Ticks: <= 0 })
        {
            await RemoveAsync(key, cancellationToken).ConfigureAwait(false);
            return 0;
        }

        var difference = await l2Cache
            .SetIfHigherAsync(key, value, expiration, cancellationToken)
            .ConfigureAwait(false);

        if (Math.Abs(difference) > double.Epsilon)
        {
            // Value was updated - we know the new value is exactly what we passed in
            var localExpiration = _GetLocalExpiration(expiration);
            await LocalCache.UpsertAsync(key, value, localExpiration, cancellationToken).ConfigureAwait(false);
        }
        else
        {
            // Value was not updated - remove from local cache since we don't know actual value
            await LocalCache.RemoveAsync(key, cancellationToken).ConfigureAwait(false);
        }

        await _PublishInvalidationAsync(
                new CacheInvalidationMessage { InstanceId = _instanceId, Key = key },
                cancellationToken
            )
            .ConfigureAwait(false);

        return difference;
    }

    /// <inheritdoc />
    public async ValueTask<long> SetIfHigherAsync(
        string key,
        long value,
        TimeSpan? expiration,
        CancellationToken cancellationToken = default
    )
    {
        _ThrowIfDisposed();
        Argument.IsNotNullOrEmpty(key);
        cancellationToken.ThrowIfCancellationRequested();

        if (expiration is { Ticks: <= 0 })
        {
            await RemoveAsync(key, cancellationToken).ConfigureAwait(false);
            return 0;
        }

        var difference = await l2Cache
            .SetIfHigherAsync(key, value, expiration, cancellationToken)
            .ConfigureAwait(false);

        if (difference != 0)
        {
            var localExpiration = _GetLocalExpiration(expiration);
            await LocalCache.UpsertAsync(key, value, localExpiration, cancellationToken).ConfigureAwait(false);
        }
        else
        {
            await LocalCache.RemoveAsync(key, cancellationToken).ConfigureAwait(false);
        }

        await _PublishInvalidationAsync(
                new CacheInvalidationMessage { InstanceId = _instanceId, Key = key },
                cancellationToken
            )
            .ConfigureAwait(false);

        return difference;
    }

    /// <inheritdoc />
    public async ValueTask<double> SetIfLowerAsync(
        string key,
        double value,
        TimeSpan? expiration,
        CancellationToken cancellationToken = default
    )
    {
        _ThrowIfDisposed();
        Argument.IsNotNullOrEmpty(key);
        cancellationToken.ThrowIfCancellationRequested();

        if (expiration is { Ticks: <= 0 })
        {
            await RemoveAsync(key, cancellationToken).ConfigureAwait(false);
            return 0;
        }

        var difference = await l2Cache.SetIfLowerAsync(key, value, expiration, cancellationToken).ConfigureAwait(false);

        if (Math.Abs(difference) > double.Epsilon)
        {
            var localExpiration = _GetLocalExpiration(expiration);
            await LocalCache.UpsertAsync(key, value, localExpiration, cancellationToken).ConfigureAwait(false);
        }
        else
        {
            await LocalCache.RemoveAsync(key, cancellationToken).ConfigureAwait(false);
        }

        await _PublishInvalidationAsync(
                new CacheInvalidationMessage { InstanceId = _instanceId, Key = key },
                cancellationToken
            )
            .ConfigureAwait(false);

        return difference;
    }

    /// <inheritdoc />
    public async ValueTask<long> SetIfLowerAsync(
        string key,
        long value,
        TimeSpan? expiration,
        CancellationToken cancellationToken = default
    )
    {
        _ThrowIfDisposed();
        Argument.IsNotNullOrEmpty(key);
        cancellationToken.ThrowIfCancellationRequested();

        if (expiration is { Ticks: <= 0 })
        {
            await RemoveAsync(key, cancellationToken).ConfigureAwait(false);
            return 0;
        }

        var difference = await l2Cache.SetIfLowerAsync(key, value, expiration, cancellationToken).ConfigureAwait(false);

        if (difference != 0)
        {
            var localExpiration = _GetLocalExpiration(expiration);
            await LocalCache.UpsertAsync(key, value, localExpiration, cancellationToken).ConfigureAwait(false);
        }
        else
        {
            await LocalCache.RemoveAsync(key, cancellationToken).ConfigureAwait(false);
        }

        await _PublishInvalidationAsync(
                new CacheInvalidationMessage { InstanceId = _instanceId, Key = key },
                cancellationToken
            )
            .ConfigureAwait(false);

        return difference;
    }

    /// <inheritdoc />
    public async ValueTask<long> SetAddAsync<T>(
        string key,
        IEnumerable<T> value,
        TimeSpan? expiration,
        CancellationToken cancellationToken = default
    )
    {
        _ThrowIfDisposed();
        Argument.IsNotNullOrEmpty(key);
        Argument.IsNotNull(value);
        cancellationToken.ThrowIfCancellationRequested();

        var items = value as T[] ?? value.ToArray();
        var addedCount = await l2Cache.SetAddAsync(key, items, expiration, cancellationToken).ConfigureAwait(false);

        if (addedCount == items.Length)
        {
            var localExpiration = _GetLocalExpiration(expiration);
            await LocalCache.SetAddAsync(key, items, localExpiration, cancellationToken).ConfigureAwait(false);
        }
        else
        {
            // Partial success - remove to force re-fetch
            await LocalCache.RemoveAsync(key, cancellationToken).ConfigureAwait(false);
        }

        await _PublishInvalidationAsync(
                new CacheInvalidationMessage { InstanceId = _instanceId, Key = key },
                cancellationToken
            )
            .ConfigureAwait(false);

        return addedCount;
    }

    #endregion

    #region ICache - Remove Operations

    /// <inheritdoc />
    public async ValueTask<bool> RemoveAsync(string key, CancellationToken cancellationToken = default)
    {
        _ThrowIfDisposed();
        Argument.IsNotNullOrEmpty(key);
        cancellationToken.ThrowIfCancellationRequested();

        bool removed;

        try
        {
            removed = await l2Cache.RemoveAsync(key, cancellationToken).ConfigureAwait(false);
            RecoveryQueue?.OnSuccessfulL2Operation(key);
        }
        catch (Exception exception)
            when (!FactoryCacheCoordinator.IsCallerCancellation(exception, cancellationToken)
                && (RecoveryQueue is not null || options.DistributedCacheCircuitBreakerDuration > TimeSpan.Zero)
            )
        {
            // Trip the breaker on a configured-breaker or recovery-enabled L2 failure so concurrent callers stop
            // hammering the down L2 — independent of auto-recovery, matching UpsertAllAsync. (No-op when the
            // breaker duration is zero.)
            _OpenDistributedCacheCircuit(exception, key);
            _logger.LogFailedToWriteToL2Cache(exception, key);

            if (RecoveryQueue is null)
            {
                // No recovery queue to replay the removal: surface the failure so the caller knows the L2 remove
                // may not have applied.
                throw;
            }

            // Degraded mode: L1 is removed below, the L2 removal is queued for replay, and we conservatively
            // report (and publish) the removal because the L2 state is unknown.
            _QueueRemoveRecovery(key);
            removed = true;
        }

        await LocalCache.RemoveAsync(key, cancellationToken).ConfigureAwait(false);

        // Only notify other nodes if the key actually existed and was removed
        if (removed)
        {
            await _PublishInvalidationAsync(
                    new CacheInvalidationMessage { InstanceId = _instanceId, Key = key },
                    cancellationToken,
                    queueOnFailure: true
                )
                .ConfigureAwait(false);
        }

        return removed;
    }

    /// <inheritdoc />
    public async ValueTask<bool> ExpireAsync(string key, CancellationToken cancellationToken = default)
    {
        _ThrowIfDisposed();
        Argument.IsNotNullOrEmpty(key);
        cancellationToken.ThrowIfCancellationRequested();

        bool expired;

        try
        {
            expired = await l2Cache.ExpireAsync(key, cancellationToken).ConfigureAwait(false);
            RecoveryQueue?.OnSuccessfulL2Operation(key);
        }
        catch (Exception exception)
            when (!FactoryCacheCoordinator.IsCallerCancellation(exception, cancellationToken)
                && (RecoveryQueue is not null || options.DistributedCacheCircuitBreakerDuration > TimeSpan.Zero)
            )
        {
            // Trip the breaker on a configured-breaker or recovery-enabled L2 failure so concurrent callers stop
            // hammering the down L2 — independent of auto-recovery, mirrors RemoveAsync. (No-op when the breaker
            // duration is zero.)
            _OpenDistributedCacheCircuit(exception, key);
            _logger.LogFailedToWriteToL2Cache(exception, key);

            if (RecoveryQueue is null)
            {
                // No recovery queue to replay the expiration: surface the failure so the caller knows the L2
                // expire may not have applied.
                throw;
            }

            // Degraded mode: L1 is expired below, the L2 expiration is queued for replay, and we conservatively
            // report (and publish) the expiration because the L2 state is unknown — mirrors RemoveAsync.
            _QueueExpireRecovery(key);
            expired = true;
        }

        // Logically expire the local copy too: a peer's reserve is preserved, and so is ours.
        await LocalCache.ExpireAsync(key, cancellationToken).ConfigureAwait(false);

        // Only notify other nodes if the key actually existed; the Expire flag tells receivers to logically
        // expire their L1 copy (preserving its fail-safe reserve) rather than remove it.
        if (expired)
        {
            await _PublishInvalidationAsync(
                    new CacheInvalidationMessage
                    {
                        InstanceId = _instanceId,
                        Key = key,
                        Expire = true,
                    },
                    cancellationToken,
                    queueOnFailure: true
                )
                .ConfigureAwait(false);
        }

        return expired;
    }

    /// <inheritdoc />
    public async ValueTask<bool> RemoveIfEqualAsync<T>(
        string key,
        T? expected,
        CancellationToken cancellationToken = default
    )
    {
        _ThrowIfDisposed();
        Argument.IsNotNullOrEmpty(key);
        cancellationToken.ThrowIfCancellationRequested();

        var removed = await l2Cache.RemoveIfEqualAsync(key, expected, cancellationToken).ConfigureAwait(false);

        // Always remove from local cache unconditionally (local cache might have stale value)
        await LocalCache.RemoveAsync(key, cancellationToken).ConfigureAwait(false);

        // Only notify other nodes if the key was actually removed from distributed cache
        if (removed)
        {
            await _PublishInvalidationAsync(
                    new CacheInvalidationMessage { InstanceId = _instanceId, Key = key },
                    cancellationToken
                )
                .ConfigureAwait(false);
        }

        return removed;
    }

    /// <inheritdoc />
    public async ValueTask<int> RemoveAllAsync(
        IEnumerable<string> cacheKeys,
        CancellationToken cancellationToken = default
    )
    {
        _ThrowIfDisposed();
        cancellationToken.ThrowIfCancellationRequested();

        var items = cacheKeys?.ToArray();
        var flushAll = items is null or { Length: 0 };

        int removed;

        try
        {
            removed = await l2Cache.RemoveAllAsync(items!, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (!FactoryCacheCoordinator.IsCallerCancellation(exception, cancellationToken))
        {
            // Bulk ops are not captured by auto-recovery in v1 (issue #440); surface the L2 failure for
            // observability and rethrow so the caller is not told the bulk remove succeeded.
            if (items is { Length: > 0 })
            {
                _OpenDistributedCacheCircuit(exception, items[0]);
            }

            _logger.LogFailedBulkL2CacheOperation(exception, items?.Length ?? 0);
            throw;
        }

        await LocalCache.RemoveAllAsync(items!, cancellationToken).ConfigureAwait(false);

        // Only notify other nodes if keys were actually removed
        if (removed > 0)
        {
            await _PublishInvalidationAsync(
                    new CacheInvalidationMessage
                    {
                        InstanceId = _instanceId,
                        FlushAll = flushAll,
                        Keys = items,
                    },
                    cancellationToken
                )
                .ConfigureAwait(false);
        }

        return removed;
    }

    /// <inheritdoc />
    public async ValueTask<int> RemoveByPrefixAsync(string prefix, CancellationToken cancellationToken = default)
    {
        _ThrowIfDisposed();
        Argument.IsNotNull(prefix);
        cancellationToken.ThrowIfCancellationRequested();

        var removed = await l2Cache.RemoveByPrefixAsync(prefix, cancellationToken).ConfigureAwait(false);
        await LocalCache.RemoveByPrefixAsync(prefix, cancellationToken).ConfigureAwait(false);

        // Only notify other nodes if keys were actually removed
        if (removed > 0)
        {
            await _PublishInvalidationAsync(
                    new CacheInvalidationMessage { InstanceId = _instanceId, Prefix = prefix },
                    cancellationToken
                )
                .ConfigureAwait(false);
        }

        return removed;
    }

    /// <inheritdoc />
    public async ValueTask<int> RemoveByTagAsync(string tag, CancellationToken cancellationToken = default)
    {
        _ThrowIfDisposed();
        Argument.IsNotNullOrEmpty(tag);
        cancellationToken.ThrowIfCancellationRequested();

        // Publish FIRST (matching the write-path ordering rationale: minimize the window in which another
        // instance re-populates its L1 from a not-yet-invalidated L2), then invalidate L2, then our own L1.
        await _PublishInvalidationAsync(
                new CacheInvalidationMessage { InstanceId = _instanceId, Tag = tag },
                cancellationToken
            )
            .ConfigureAwait(false);

        var removed = await l2Cache.RemoveByTagAsync(tag, cancellationToken).ConfigureAwait(false);
        await LocalCache.RemoveByTagAsync(tag, cancellationToken).ConfigureAwait(false);

        return removed;
    }

    /// <inheritdoc />
    public async ValueTask<long> SetRemoveAsync<T>(
        string key,
        IEnumerable<T> value,
        TimeSpan? expiration,
        CancellationToken cancellationToken = default
    )
    {
        _ThrowIfDisposed();
        Argument.IsNotNullOrEmpty(key);
        Argument.IsNotNull(value);
        cancellationToken.ThrowIfCancellationRequested();

        var items = value as T[] ?? value.ToArray();
        var removedCount = await l2Cache
            .SetRemoveAsync(key, items, expiration, cancellationToken)
            .ConfigureAwait(false);

        if (removedCount == items.Length)
        {
            await LocalCache.SetRemoveAsync(key, items, expiration, cancellationToken).ConfigureAwait(false);
        }
        else
        {
            // Partial success - remove to force re-fetch
            await LocalCache.RemoveAsync(key, cancellationToken).ConfigureAwait(false);
        }

        // Only notify other nodes if items were actually removed
        if (removedCount > 0)
        {
            await _PublishInvalidationAsync(
                    new CacheInvalidationMessage { InstanceId = _instanceId, Key = key },
                    cancellationToken
                )
                .ConfigureAwait(false);
        }

        return removedCount;
    }

    /// <inheritdoc />
    public async ValueTask FlushAsync(CancellationToken cancellationToken = default)
    {
        _ThrowIfDisposed();
        cancellationToken.ThrowIfCancellationRequested();

        await l2Cache.FlushAsync(cancellationToken).ConfigureAwait(false);
        await LocalCache.FlushAsync(cancellationToken).ConfigureAwait(false);
        await _PublishInvalidationAsync(
                new CacheInvalidationMessage { InstanceId = _instanceId, FlushAll = true },
                cancellationToken
            )
            .ConfigureAwait(false);
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

    private void _QueueScalarUpsertRecovery<T>(string key, T? value, TimeSpan? expiration)
    {
        var queue = RecoveryQueue!;
        var now = _timeProvider.GetUtcNow();

        // Values without a TTL have no natural item expiry; bound them by the generous fixed window.
        var deadline = expiration.HasValue ? now + expiration.Value : now + queue.DefaultRetention;

        queue.Enqueue(
            key,
            HybridCacheRecoveryKind.SetEntry,
            deadline,
            async ct =>
            {
                // L1 is the source of truth: if the entry is gone, the queued write is obsolete. A surviving
                // queued item implies no newer single-key write went through this instance (newer ops replace
                // or clear it), and foreign writes drop it via the invalidation conflict check.
                var current = await LocalCache.GetAsync<T>(key, ct).ConfigureAwait(false);

                if (!current.HasValue)
                {
                    return HybridCacheRecoveryReplayOutcome.Obsolete;
                }

                TimeSpan? remaining = expiration.HasValue ? deadline - _timeProvider.GetUtcNow() : null;

                if (remaining is { Ticks: <= 0 })
                {
                    return HybridCacheRecoveryReplayOutcome.Obsolete;
                }

                await l2Cache.UpsertAsync(key, value, remaining, ct).ConfigureAwait(false);
                await _PublishReplayedInvalidationAsync(key, now, ct).ConfigureAwait(false);
                return HybridCacheRecoveryReplayOutcome.Replayed;
            }
        );
    }

    private void _QueueSetEntryRecovery<T>(string key, CacheStoreEntryWrite<T> entry, DateTime? l1PhysicalStamp)
    {
        var queue = RecoveryQueue!;
        var writeTimestamp = _timeProvider.GetUtcNow();

        queue.Enqueue(
            key,
            HybridCacheRecoveryKind.SetEntry,
            new DateTimeOffset(DateTime.SpecifyKind(entry.PhysicalExpiresAt, DateTimeKind.Utc)),
            async ct =>
            {
                // L1 is the source of truth: only replay if the L1 entry still exists and carries the exact
                // physical stamp this write produced — a different stamp means the entry changed and the
                // queued write is obsolete.
                if (LocalCache is IFactoryCacheStore l1Store && l1PhysicalStamp.HasValue)
                {
                    var current = await l1Store.TryGetEntryAsync<T>(key, ct).ConfigureAwait(false);

                    if (!current.Found || current.PhysicalExpiresAt != l1PhysicalStamp)
                    {
                        return HybridCacheRecoveryReplayOutcome.Obsolete;
                    }
                }
                else
                {
                    var current = await LocalCache.GetAsync<T>(key, ct).ConfigureAwait(false);

                    if (!current.HasValue)
                    {
                        return HybridCacheRecoveryReplayOutcome.Obsolete;
                    }
                }

                if (l2Cache is IFactoryCacheStore l2Store)
                {
                    // The descriptor carries absolute UTC stamps, so it replays unchanged.
                    await l2Store.SetEntryAsync(key, entry, ct).ConfigureAwait(false);
                }
                else
                {
                    var expiresIn = (
                        entry.SlidingExpiration is null ? entry.PhysicalExpiresAt : entry.LogicalExpiresAt
                    ).Subtract(_GetUtcNow());

                    if (expiresIn <= TimeSpan.Zero)
                    {
                        return HybridCacheRecoveryReplayOutcome.Obsolete;
                    }

                    await l2Cache
                        .UpsertAsync(key, entry.IsNull ? default : entry.Value, expiresIn, ct)
                        .ConfigureAwait(false);
                }

                // Mirror the live SetEntry path, restamp gating included: peers' cached bytes are identical
                // for a metadata-only restamp, so only value-bearing replays broadcast.
                if (!entry.IsRestamp)
                {
                    await _PublishReplayedInvalidationAsync(key, writeTimestamp, ct).ConfigureAwait(false);
                }

                return HybridCacheRecoveryReplayOutcome.Replayed;
            }
        );
    }

    private void _QueueRemoveRecovery(string key)
    {
        var queue = RecoveryQueue!;
        var removeTimestamp = _timeProvider.GetUtcNow();

        queue.Enqueue(
            key,
            HybridCacheRecoveryKind.Remove,
            removeTimestamp + queue.DefaultRetention,
            async ct =>
            {
                await l2Cache.RemoveAsync(key, ct).ConfigureAwait(false);
                await _PublishReplayedInvalidationAsync(key, removeTimestamp, ct).ConfigureAwait(false);
                return HybridCacheRecoveryReplayOutcome.Replayed;
            }
        );
    }

    private void _QueueExpireRecovery(string key)
    {
        var queue = RecoveryQueue!;
        var expireTimestamp = _timeProvider.GetUtcNow();

        queue.Enqueue(
            key,
            HybridCacheRecoveryKind.Expire,
            expireTimestamp + queue.DefaultRetention,
            async ct =>
            {
                await l2Cache.ExpireAsync(key, ct).ConfigureAwait(false);
                await _PublishReplayedInvalidationAsync(key, expireTimestamp, ct, expire: true).ConfigureAwait(false);
                return HybridCacheRecoveryReplayOutcome.Replayed;
            }
        );
    }

    private void _QueuePublishRecovery(CacheInvalidationMessage message)
    {
        var queue = RecoveryQueue!;

        queue.Enqueue(
            message.Key!,
            HybridCacheRecoveryKind.PublishInvalidation,
            _timeProvider.GetUtcNow() + queue.DefaultRetention,
            async ct =>
            {
                // Re-publish the captured message unchanged (original timestamp) so receivers can still order
                // it correctly against operations that happened after the original publish attempt.
                await publisher.PublishAsync(message, cancellationToken: ct).ConfigureAwait(false);
                return HybridCacheRecoveryReplayOutcome.Replayed;
            },
            // The item's intent timestamp is the original publish time, not now: a foreign write that raced in
            // between must still win the incoming-invalidation conflict check against this pending publish.
            enqueuedAt: message.Timestamp
        );
    }

    /// <summary>
    /// Publishes the key invalidation for a successfully replayed value op, mirroring the live path (a landed
    /// set/remove subsumes its invalidation). Stamped with the ORIGINAL write time so receivers order it
    /// correctly: a peer whose own pending write for the key is newer ignores it instead of wiping its L1. A
    /// publish failure queues a residual PublishInvalidation — the value already landed in L2, so a pending
    /// publish is the correct remaining intent; it replaces the value op being replayed and inherits the normal
    /// retry cap, so the failure path cannot loop unboundedly.
    /// </summary>
    private async ValueTask _PublishReplayedInvalidationAsync(
        string key,
        DateTimeOffset writeTimestamp,
        CancellationToken ct,
        bool expire = false
    )
    {
        await _PublishInvalidationAsync(
                new CacheInvalidationMessage
                {
                    InstanceId = _instanceId,
                    Key = key,
                    Expire = expire,
                    Timestamp = writeTimestamp,
                },
                ct,
                queueOnFailure: true
            )
            .ConfigureAwait(false);
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
