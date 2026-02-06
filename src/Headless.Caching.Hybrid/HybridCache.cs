// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;
using Headless.Messaging;
using Headless.Threading;
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
/// <item>Execute factory if both miss, populate both caches</item>
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
    IDistributedCache l2Cache,
    IDirectPublisher publisher,
    HybridCacheOptions options,
    ILogger<HybridCache>? logger = null
) : ICache, IAsyncDisposable
{
    private readonly ILogger _logger = logger ?? NullLogger<HybridCache>.Instance;
    private readonly string _instanceId = options.InstanceId ?? Guid.NewGuid().ToString("N");
    private readonly KeyedAsyncLock _keyedLock = new();

    private long _localCacheHits;
    private long _invalidateCacheCalls;
    private int _isDisposed;

    /// <summary>Gets the number of L1 cache hits.</summary>
    public long LocalCacheHits => Interlocked.Read(ref _localCacheHits);

    /// <summary>Gets the number of invalidation calls received from other instances.</summary>
    public long InvalidateCacheCalls => Interlocked.Read(ref _invalidateCacheCalls);

    /// <summary>Provides direct access to the L1 (in-memory) cache for advanced scenarios.</summary>
    public IInMemoryCache LocalCache { get; } = l1Cache;

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

        if (message.Keys is { Length: > 0 })
        {
            await LocalCache.RemoveAllAsync(message.Keys, ct).ConfigureAwait(false);
            return;
        }

        if (!string.IsNullOrEmpty(message.Key))
        {
            await LocalCache.RemoveAsync(message.Key, ct).ConfigureAwait(false);
            return;
        }

        _logger.LogUnknownInvalidateCacheMessage();
    }

    #region ICache - Get Operations

    /// <inheritdoc />
    public async ValueTask<CacheValue<T>> GetOrAddAsync<T>(
        string key,
        Func<CancellationToken, ValueTask<T?>> factory,
        TimeSpan expiration,
        CancellationToken cancellationToken = default
    )
    {
        _ThrowIfDisposed();
        Argument.IsNotNullOrEmpty(key);
        Argument.IsNotNull(factory);
        Argument.IsPositive(expiration);
        cancellationToken.ThrowIfCancellationRequested();

        // Check L1 first
        var cacheValue = await LocalCache.GetAsync<T>(key, cancellationToken).ConfigureAwait(false);
        if (cacheValue.HasValue)
        {
            _logger.LogLocalCacheHit(key);
            Interlocked.Increment(ref _localCacheHits);
            return cacheValue;
        }

        _logger.LogLocalCacheMiss(key);

        // Stampede protection + factory
        using (await _keyedLock.LockAsync(key, cancellationToken).ConfigureAwait(false))
        {
            // Double-check L1 after acquiring lock (another thread may have populated it)
            cacheValue = await LocalCache.GetAsync<T>(key, cancellationToken).ConfigureAwait(false);
            if (cacheValue.HasValue)
            {
                return cacheValue;
            }

            // Double-check L2 after acquiring lock (another instance may have populated it)
            cacheValue = await l2Cache.GetAsync<T>(key, cancellationToken).ConfigureAwait(false);
            if (cacheValue.HasValue)
            {
                await LocalCache
                    .UpsertAsync(key, cacheValue.Value, _GetLocalExpiration(expiration), cancellationToken)
                    .ConfigureAwait(false);
                return cacheValue;
            }

            // Execute factory
            var value = await factory(cancellationToken).ConfigureAwait(false);

            // Populate L2 first (with exception handling)
            try
            {
                await l2Cache.UpsertAsync(key, value, expiration, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // L2 failure is non-fatal: log and continue to populate L1
                _logger.LogFailedToWriteToL2Cache(ex, key);
            }

            // Populate L1
            await LocalCache
                .UpsertAsync(key, value, _GetLocalExpiration(expiration), cancellationToken)
                .ConfigureAwait(false);

            return new CacheValue<T>(value, hasValue: true);
        }
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

        // Collect values to batch upsert to L1 (avoid N+1 pattern)
        var valuesToCache = new Dictionary<string, T>(StringComparer.Ordinal);
        foreach (var kvp in distributedResults)
        {
            result[kvp.Key] = kvp.Value;
            if (kvp.Value is { HasValue: true, Value: not null })
            {
                valuesToCache[kvp.Key] = kvp.Value.Value;
            }
        }

        // Batch upsert to L1 using DefaultLocalExpiration
        if (valuesToCache.Count > 0)
        {
            var localExpiration = _GetLocalExpiration(null);
            _logger.LogBatchSettingLocalCacheKeys(valuesToCache.Count, localExpiration);
            await LocalCache.UpsertAllAsync(valuesToCache, localExpiration, cancellationToken).ConfigureAwait(false);
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
    public async ValueTask<int> GetCountAsync(string prefix = "", CancellationToken cancellationToken = default)
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

        var updated = await l2Cache.UpsertAsync(key, value, expiration, cancellationToken).ConfigureAwait(false);

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
                cancellationToken
            )
            .ConfigureAwait(false);

        return updated;
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

        var removed = await l2Cache.RemoveAsync(key, cancellationToken).ConfigureAwait(false);
        await LocalCache.RemoveAsync(key, cancellationToken).ConfigureAwait(false);

        // Only notify other nodes if the key actually existed and was removed
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

    private async ValueTask _PublishInvalidationAsync(CacheInvalidationMessage message, CancellationToken ct)
    {
        try
        {
            await publisher.PublishAsync(message, ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Publish failure is non-fatal: other instances may have stale L1 data
            // until their TTL expires. This is acceptable for eventual consistency.
            _logger.LogFailedToPublishCacheInvalidation(
                ex,
                message.Keys?.Length ?? (message.Key is not null ? 1 : 0),
                message.Prefix is not null,
                message.FlushAll
            );
        }
    }

    private void _ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(_isDisposed == 1, this);
    }

    private TimeSpan? _GetLocalExpiration(TimeSpan? expiration) => options.DefaultLocalExpiration ?? expiration;

    #endregion

    #region IAsyncDisposable

    /// <inheritdoc />
    public ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _isDisposed, 1) == 1)
        {
            return ValueTask.CompletedTask;
        }

        _keyedLock.Dispose();
        return ValueTask.CompletedTask;
    }

    #endregion
}
