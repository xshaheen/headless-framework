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
public sealed class HybridCache(
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
                await LocalCache.RemoveAllAsync(keys, ct).ConfigureAwait(false);
            }

            return;
        }

        if (!string.IsNullOrEmpty(message.Key))
        {
            if (!_ShouldIgnoreInvalidationFor(message.Key))
            {
                await LocalCache.RemoveAsync(message.Key, ct).ConfigureAwait(false);
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
        cacheValue = await l2Cache.GetAsync<T>(key, cancellationToken).ConfigureAwait(false);

        if (cacheValue.HasValue)
        {
            var expiration = await l2Cache.GetExpirationAsync(key, cancellationToken).ConfigureAwait(false);
            var localExpiration = _GetLocalExpiration(expiration);
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
        var distributedResults = await l2Cache.GetAllAsync<T>(missedKeys, cancellationToken).ConfigureAwait(false);

        var valuesToCache = new Dictionary<string, (T Value, TimeSpan? Expiration)>(StringComparer.Ordinal);
        foreach (var kvp in distributedResults)
        {
            result[kvp.Key] = kvp.Value;
            if (kvp.Value is { HasValue: true, Value: not null })
            {
                var expiration = await l2Cache.GetExpirationAsync(kvp.Key, cancellationToken).ConfigureAwait(false);
                valuesToCache[kvp.Key] = (kvp.Value.Value, _GetLocalExpiration(expiration));
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
        return await l2Cache.ExistsAsync(key, cancellationToken).ConfigureAwait(false);
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
        return await l2Cache.GetExpirationAsync(key, cancellationToken).ConfigureAwait(false);
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
        cacheValue = await l2Cache.GetSetAsync<T>(key, pageIndex, pageSize, cancellationToken).ConfigureAwait(false);

        if (cacheValue.HasValue)
        {
            var expiration = await l2Cache.GetExpirationAsync(key, cancellationToken).ConfigureAwait(false);
            var localExpiration = _GetLocalExpiration(expiration);
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

        bool updated;

        try
        {
            updated = await l2Cache.UpsertAsync(key, value, expiration, cancellationToken).ConfigureAwait(false);
            RecoveryQueue?.OnSuccessfulL2Operation(key);
        }
        catch (Exception exception)
            when (RecoveryQueue is not null
                && !FactoryCacheCoordinator.IsCallerCancellation(exception, cancellationToken)
            )
        {
            // Degraded mode: the caller succeeds against L1 and the L2 write is queued for replay.
            _logger.LogFailedToWriteToL2Cache(exception, key);
            _QueueScalarUpsertRecovery(key, value, expiration);
            updated = true;
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

        var setCount = await l2Cache.UpsertAllAsync(value, expiration, cancellationToken).ConfigureAwait(false);

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
            when (RecoveryQueue is not null
                && !FactoryCacheCoordinator.IsCallerCancellation(exception, cancellationToken)
            )
        {
            // Degraded mode: L1 is removed below, the L2 removal is queued for replay, and we conservatively
            // report (and publish) the removal because the L2 state is unknown.
            _logger.LogFailedToWriteToL2Cache(exception, key);
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

        var removed = await l2Cache.RemoveAllAsync(items!, cancellationToken).ConfigureAwait(false);
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
        CancellationToken ct
    )
    {
        await _PublishInvalidationAsync(
                new CacheInvalidationMessage
                {
                    InstanceId = _instanceId,
                    Key = key,
                    Timestamp = writeTimestamp,
                },
                ct,
                queueOnFailure: true
            )
            .ConfigureAwait(false);
    }

    private void _ThrowIfDisposed()
    {
        Ensure.NotDisposed(Volatile.Read(ref _isDisposed) == 1, this);
    }

    async ValueTask<CacheStoreEntry<T>> IFactoryCacheStore.TryGetEntryAsync<T>(
        string key,
        CancellationToken cancellationToken
    )
    {
        _ThrowIfDisposed();
        Argument.IsNotNullOrEmpty(key);
        cancellationToken.ThrowIfCancellationRequested();

        var now = _GetUtcNow();
        CacheStoreEntry<T>? l1StaleCandidate = null;
        var l1SlidingHit = false;

        if (LocalCache is IFactoryCacheStore l1Store)
        {
            var l1Entry = await l1Store.TryGetEntryAsync<T>(key, cancellationToken).ConfigureAwait(false);

            if (l1Entry.IsFresh(now))
            {
                _logger.LogLocalCacheHit(key);
                Interlocked.Increment(ref _localCacheHits);

                if (!l1Entry.SlidingExpiration.HasValue)
                {
                    return l1Entry;
                }

                // Sliding entries need the L2 physical cap for safe re-arm. A local entry may be physically
                // capped by DefaultLocalExpiration, so use it only as a no-rearm fallback if L2 is unavailable.
                l1SlidingHit = true;
                l1StaleCandidate = l1Entry with { SlidingExpiration = null };
            }
            else if (l1Entry.IsPhysicallyPresent(now))
            {
                l1StaleCandidate = l1Entry;
            }
        }
        else
        {
            var l1Value = await LocalCache.GetAsync<T>(key, cancellationToken).ConfigureAwait(false);

            if (l1Value.HasValue)
            {
                _logger.LogLocalCacheHit(key);
                Interlocked.Increment(ref _localCacheHits);
                return new CacheStoreEntry<T>(
                    Found: true,
                    IsNull: l1Value.IsNull,
                    Value: l1Value.Value,
                    LogicalExpiresAt: null,
                    PhysicalExpiresAt: null,
                    SlidingExpiration: null
                );
            }
        }

        if (!l1SlidingHit)
        {
            _logger.LogLocalCacheMiss(key);
        }

        if (l2Cache is not IFactoryCacheStore l2Store)
        {
            return l1StaleCandidate ?? CacheStoreEntry<T>.NotFound;
        }

        CacheStoreEntry<T> l2Entry;

        try
        {
            l2Entry = await l2Store.TryGetEntryAsync<T>(key, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (!FactoryCacheCoordinator.IsCallerCancellation(exception, cancellationToken))
        {
            _logger.LogFailedToReadFromL2Cache(exception, key);
            return l1StaleCandidate ?? CacheStoreEntry<T>.NotFound;
        }

        // Only promote a logically-fresh L2 entry into L1. Promoting a stale (logically-expired) reserve on
        // every fail-safe read amplifies L1 writes under stampede and can overwrite a newer L1 stale reserve.
        // The coordinator still receives the returned l2Entry as its stale candidate, so fail-safe serving of
        // an L2 reserve is unaffected — it simply is not re-cached into L1.
        if (l2Entry.IsFresh(now) && LocalCache is IFactoryCacheStore l1StoreForPromotion)
        {
            await _SetLocalEntryAsync(l1StoreForPromotion, key, l2Entry, cancellationToken).ConfigureAwait(false);
        }

        return l2Entry.Found ? l2Entry : l1StaleCandidate ?? CacheStoreEntry<T>.NotFound;
    }

    // Non-async forwarder: `in` parameters are not allowed on async methods, so copy the descriptor by value.
    ValueTask IFactoryCacheStore.SetEntryAsync<T>(
        string key,
        in CacheStoreEntryWrite<T> entry,
        CancellationToken cancellationToken
    )
        where T : default
    {
        _ThrowIfDisposed();
        Argument.IsNotNullOrEmpty(key);
        cancellationToken.ThrowIfCancellationRequested();

        return _SetEntryCoreAsync(key, entry, cancellationToken);
    }

    private async ValueTask _SetEntryCoreAsync<T>(
        string key,
        CacheStoreEntryWrite<T> entry,
        CancellationToken cancellationToken
    )
    {
        var l2WriteSucceeded = false;

        if (l2Cache is IFactoryCacheStore l2Store)
        {
            try
            {
                // Pass the descriptor through unchanged so per-entry metadata round-trips the L2 tier.
                await l2Store.SetEntryAsync(key, in entry, cancellationToken).ConfigureAwait(false);
                l2WriteSucceeded = true;
            }
            catch (Exception exception)
                when (!FactoryCacheCoordinator.IsCallerCancellation(exception, cancellationToken))
            {
                _logger.LogFailedToWriteToL2Cache(exception, key);
            }
        }
        else
        {
            var expiresIn = (
                entry.SlidingExpiration is null ? entry.PhysicalExpiresAt : entry.LogicalExpiresAt
            ).Subtract(_GetUtcNow());

            try
            {
                await l2Cache
                    .UpsertAsync(key, entry.IsNull ? default : entry.Value, expiresIn, cancellationToken)
                    .ConfigureAwait(false);
                l2WriteSucceeded = true;
            }
            catch (Exception exception)
                when (!FactoryCacheCoordinator.IsCallerCancellation(exception, cancellationToken))
            {
                _logger.LogFailedToWriteToL2Cache(exception, key);
            }
        }

        DateTime? l1PhysicalStamp = null;

        if (LocalCache is IFactoryCacheStore l1Store)
        {
            var l1Entry = new CacheStoreEntry<T>(
                Found: true,
                IsNull: entry.IsNull,
                Value: entry.IsNull ? default : entry.Value,
                LogicalExpiresAt: entry.LogicalExpiresAt,
                PhysicalExpiresAt: entry.PhysicalExpiresAt,
                SlidingExpiration: entry.SlidingExpiration
            )
            {
                EagerRefreshAt = entry.EagerRefreshAt,
                ETag = entry.ETag,
                LastModifiedAt = entry.LastModifiedAt,
                Tags = entry.Tags,
            };

            l1PhysicalStamp = await _SetLocalEntryAsync(l1Store, key, l1Entry, cancellationToken).ConfigureAwait(false);
        }
        else
        {
            await LocalCache
                .UpsertAsync(
                    key,
                    entry.IsNull ? default : entry.Value,
                    _GetLocalExpiration(entry.PhysicalExpiresAt.Subtract(_GetUtcNow())),
                    cancellationToken
                )
                .ConfigureAwait(false);
        }

        if (RecoveryQueue is not null)
        {
            if (l2WriteSucceeded)
            {
                RecoveryQueue.OnSuccessfulL2Operation(key);
            }
            else
            {
                // Degraded mode: the caller already succeeded against L1; queue the L2 write for replay.
                _QueueSetEntryRecovery(key, entry, l1PhysicalStamp);
            }
        }

        // Factory value-writes (cold-miss fresh write, soft-timeout background completion, eager refresh,
        // conditional Modified) invalidate peers' L1 exactly like the explicit-upsert path. Metadata-only
        // restamps (NotModified extension, fail-safe throttle, eager-refresh gate) are skipped: peers' cached
        // bytes are still identical, so invalidating them would only force pointless L2 re-reads. The publish
        // runs after the recovery bookkeeping so a queued publish-recovery item cannot be cleared by this
        // write's own L2 success.
        if (!entry.IsRestamp)
        {
            await _PublishInvalidationAsync(
                    new CacheInvalidationMessage { InstanceId = _instanceId, Key = key },
                    cancellationToken,
                    queueOnFailure: true
                )
                .ConfigureAwait(false);
        }
    }

    async ValueTask IFactoryCacheStore.TryRearmSlidingAsync(
        string key,
        TimeSpan slidingExpiration,
        DateTime physicalExpiresAt,
        DateTime now,
        CancellationToken cancellationToken
    )
    {
        _ThrowIfDisposed();
        Argument.IsNotNullOrEmpty(key);
        cancellationToken.ThrowIfCancellationRequested();

        // Re-arm both tiers (KTD-8). L2 carries the authoritative physical cap; L1's own re-arm bounds the new
        // logical deadline by its locally-capped entry metadata, so passing the L2 cap is safe. L2 is best-effort
        // (a remote hiccup must not fail the read); L1 is in-process and effectively infallible.
        if (l2Cache is IFactoryCacheStore l2Store)
        {
            try
            {
                await l2Store
                    .TryRearmSlidingAsync(key, slidingExpiration, physicalExpiresAt, now, cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (Exception exception)
                when (!FactoryCacheCoordinator.IsCallerCancellation(exception, cancellationToken))
            {
                _logger.LogFailedToWriteToL2Cache(exception, key);
            }
        }

        if (LocalCache is IFactoryCacheStore l1Store)
        {
            await l1Store
                .TryRearmSlidingAsync(key, slidingExpiration, physicalExpiresAt, now, cancellationToken)
                .ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Writes an entry into L1 bounded by the local ceiling. Returns the physical expiration stamp actually
    /// written (auto-recovery uses it to detect that the L1 entry was replaced), or <see langword="null"/>
    /// when the write was skipped.
    /// </summary>
    private async ValueTask<DateTime?> _SetLocalEntryAsync<T>(
        IFactoryCacheStore l1Store,
        string key,
        CacheStoreEntry<T> entry,
        CancellationToken cancellationToken
    )
    {
        if (!entry.Found)
        {
            return null;
        }

        var now = _GetUtcNow();
        var localCeiling = options.DefaultLocalExpiration.HasValue
            ? now.Add(options.DefaultLocalExpiration.Value)
            : (DateTime?)null;

        // Legacy/unframed L2 entries carry no expiration metadata. Promote them into L1 bounded by the local
        // ceiling so they cannot pin process memory indefinitely; without a configured ceiling there is no finite
        // bound to apply, so skip rather than cache a never-expiring entry locally.
        if (!entry.LogicalExpiresAt.HasValue || !entry.PhysicalExpiresAt.HasValue)
        {
            if (!localCeiling.HasValue)
            {
                return null;
            }

            var ceilingWrite = new CacheStoreEntryWrite<T>
            {
                Value = entry.Value,
                IsNull = entry.IsNull,
                LogicalExpiresAt = localCeiling.Value,
                PhysicalExpiresAt = localCeiling.Value,
                SlidingExpiration = null,
                EagerRefreshAt = entry.EagerRefreshAt,
                ETag = entry.ETag,
                LastModifiedAt = entry.LastModifiedAt,
                Tags = entry.Tags,
            };

            await l1Store.SetEntryAsync(key, in ceilingWrite, cancellationToken).ConfigureAwait(false);

            return ceilingWrite.PhysicalExpiresAt;
        }

        var logicalExpiresAt = localCeiling.HasValue
            ? _Min(entry.LogicalExpiresAt.Value, localCeiling.Value)
            : entry.LogicalExpiresAt.Value;
        var physicalExpiresAt = localCeiling.HasValue
            ? _Min(entry.PhysicalExpiresAt.Value, localCeiling.Value)
            : entry.PhysicalExpiresAt.Value;

        var localWrite = new CacheStoreEntryWrite<T>
        {
            Value = entry.Value,
            IsNull = entry.IsNull,
            LogicalExpiresAt = logicalExpiresAt,
            PhysicalExpiresAt = physicalExpiresAt,
            SlidingExpiration = entry.SlidingExpiration,
            EagerRefreshAt = entry.EagerRefreshAt,
            ETag = entry.ETag,
            LastModifiedAt = entry.LastModifiedAt,
            Tags = entry.Tags,
        };

        await l1Store.SetEntryAsync(key, in localWrite, cancellationToken).ConfigureAwait(false);

        return localWrite.PhysicalExpiresAt;
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
