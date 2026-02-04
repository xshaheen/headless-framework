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
public sealed class HybridCache : ICache, IAsyncDisposable
{
    private readonly IInMemoryCache _l1Cache;
    private readonly IDistributedCache _l2Cache;
    private readonly IDirectPublisher _publisher;
    private readonly HybridCacheOptions _options;
    private readonly ILogger _logger;
    private readonly string _instanceId;
    private readonly KeyedAsyncLock _keyedLock = new();

    private long _localCacheHits;
    private long _invalidateCacheCalls;
    private int _isDisposed;

    /// <summary>Gets the number of L1 cache hits.</summary>
    public long LocalCacheHits => Interlocked.Read(ref _localCacheHits);

    /// <summary>Gets the number of invalidation calls received from other instances.</summary>
    public long InvalidateCacheCalls => Interlocked.Read(ref _invalidateCacheCalls);

    /// <summary>Provides direct access to the L1 (in-memory) cache for advanced scenarios.</summary>
    public IInMemoryCache LocalCache => _l1Cache;

    public HybridCache(
        IInMemoryCache l1Cache,
        IDistributedCache l2Cache,
        IDirectPublisher publisher,
        HybridCacheOptions options,
        ILogger<HybridCache>? logger = null
    )
    {
        _l1Cache = l1Cache;
        _l2Cache = l2Cache;
        _publisher = publisher;
        _options = options;
        _logger = logger ?? NullLogger<HybridCache>.Instance;
        _instanceId = options.InstanceId ?? Guid.NewGuid().ToString("N");
    }

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

        if (_logger.IsEnabled(LogLevel.Trace))
        {
            _logger.LogTrace(
                "Invalidating local cache from remote: id={CacheId} keyCount={KeyCount} hasPrefix={HasPrefix} flushAll={FlushAll}",
                message.InstanceId,
                message.Keys?.Length ?? (message.Key is not null ? 1 : 0),
                message.Prefix is not null,
                message.FlushAll
            );
        }

        Interlocked.Increment(ref _invalidateCacheCalls);

        if (message.FlushAll)
        {
            _logger.LogTrace("Flushed local cache");
            await _l1Cache.FlushAsync(ct).AnyContext();
            return;
        }

        if (!string.IsNullOrEmpty(message.Prefix))
        {
            await _l1Cache.RemoveByPrefixAsync(message.Prefix, ct).AnyContext();
            return;
        }

        if (message.Keys is { Length: > 0 })
        {
            await _l1Cache.RemoveAllAsync(message.Keys, ct).AnyContext();
            return;
        }

        if (!string.IsNullOrEmpty(message.Key))
        {
            await _l1Cache.RemoveAsync(message.Key, ct).AnyContext();
            return;
        }

        _logger.LogWarning("Unknown invalidate cache message");
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
        var cacheValue = await _l1Cache.GetAsync<T>(key, cancellationToken).AnyContext();
        if (cacheValue.HasValue)
        {
            _logger.LogTrace("Local cache hit: {Key}", key);
            Interlocked.Increment(ref _localCacheHits);
            return cacheValue;
        }

        _logger.LogTrace("Local cache miss: {Key}", key);

        // Stampede protection + factory
        using (await _keyedLock.LockAsync(key, cancellationToken).AnyContext())
        {
            // Double-check L1 after acquiring lock (another thread may have populated it)
            cacheValue = await _l1Cache.GetAsync<T>(key, cancellationToken).AnyContext();
            if (cacheValue.HasValue)
            {
                return cacheValue;
            }

            // Double-check L2 after acquiring lock (another instance may have populated it)
            cacheValue = await _l2Cache.GetAsync<T>(key, cancellationToken).AnyContext();
            if (cacheValue.HasValue)
            {
                await _l1Cache
                    .UpsertAsync(key, cacheValue.Value, _GetLocalExpiration(expiration), cancellationToken)
                    .AnyContext();
                return cacheValue;
            }

            // Execute factory
            var value = await factory(cancellationToken).AnyContext();

            // Populate L2 first (with exception handling)
            try
            {
                await _l2Cache.UpsertAsync(key, value, expiration, cancellationToken).AnyContext();
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // L2 failure is non-fatal: log and continue to populate L1
                _logger.LogWarning(ex, "Failed to write to L2 cache for key {Key}, L1 will still be populated", key);
            }

            // Populate L1
            await _l1Cache.UpsertAsync(key, value, _GetLocalExpiration(expiration), cancellationToken).AnyContext();

            return new CacheValue<T>(value, hasValue: true);
        }
    }

    /// <inheritdoc />
    public async ValueTask<CacheValue<T>> GetAsync<T>(string key, CancellationToken cancellationToken = default)
    {
        _ThrowIfDisposed();
        Argument.IsNotNullOrEmpty(key);
        cancellationToken.ThrowIfCancellationRequested();

        var cacheValue = await _l1Cache.GetAsync<T>(key, cancellationToken).AnyContext();
        if (cacheValue.HasValue)
        {
            _logger.LogTrace("Local cache hit: {Key}", key);
            Interlocked.Increment(ref _localCacheHits);
            return cacheValue;
        }

        _logger.LogTrace("Local cache miss: {Key}", key);
        cacheValue = await _l2Cache.GetAsync<T>(key, cancellationToken).AnyContext();

        if (cacheValue.HasValue)
        {
            var expiration = await _l2Cache.GetExpirationAsync(key, cancellationToken).AnyContext();
            var localExpiration = _GetLocalExpiration(expiration);
            _logger.LogTrace("Setting local cache key: {Key} with expiration: {Expiration}", key, localExpiration);
            await _l1Cache.UpsertAsync(key, cacheValue.Value, localExpiration, cancellationToken).AnyContext();
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

        var localValues = await _l1Cache.GetAllAsync<T>(keysCollection, cancellationToken).AnyContext();

        // Collect keys that weren't found in local cache
        var missedKeys = new List<string>(keysCollection.Count);
        foreach (var kvp in localValues)
        {
            if (kvp.Value.HasValue)
            {
                _logger.LogTrace("Local cache hit: {Key}", kvp.Key);
            }
            else
            {
                _logger.LogTrace("Local cache miss: {Key}", kvp.Key);
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
        var distributedResults = await _l2Cache.GetAllAsync<T>(missedKeys, cancellationToken).AnyContext();

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
            _logger.LogTrace(
                "Batch setting {Count} local cache keys with expiration: {Expiration}",
                valuesToCache.Count,
                localExpiration
            );
            await _l1Cache.UpsertAllAsync(valuesToCache, localExpiration, cancellationToken).AnyContext();
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
        return await _l2Cache.GetByPrefixAsync<T>(prefix, cancellationToken).AnyContext();
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

        return await _l2Cache.GetAllKeysByPrefixAsync(prefix, cancellationToken).AnyContext();
    }

    /// <inheritdoc />
    public async ValueTask<int> GetCountAsync(string prefix = "", CancellationToken cancellationToken = default)
    {
        _ThrowIfDisposed();
        cancellationToken.ThrowIfCancellationRequested();

        return await _l2Cache.GetCountAsync(prefix, cancellationToken).AnyContext();
    }

    /// <inheritdoc />
    public async ValueTask<bool> ExistsAsync(string key, CancellationToken cancellationToken = default)
    {
        _ThrowIfDisposed();
        Argument.IsNotNullOrEmpty(key);
        cancellationToken.ThrowIfCancellationRequested();

        // Check local cache first
        var localExists = await _l1Cache.ExistsAsync(key, cancellationToken).AnyContext();
        if (localExists)
        {
            _logger.LogTrace("Local cache hit: {Key}", key);
            Interlocked.Increment(ref _localCacheHits);
            return true;
        }

        _logger.LogTrace("Local cache miss: {Key}", key);
        return await _l2Cache.ExistsAsync(key, cancellationToken).AnyContext();
    }

    /// <inheritdoc />
    public async ValueTask<TimeSpan?> GetExpirationAsync(string key, CancellationToken cancellationToken = default)
    {
        _ThrowIfDisposed();
        Argument.IsNotNullOrEmpty(key);
        cancellationToken.ThrowIfCancellationRequested();

        // Check if key exists in local cache first
        var localExists = await _l1Cache.ExistsAsync(key, cancellationToken).AnyContext();
        if (localExists)
        {
            _logger.LogTrace("Local cache hit: {Key}", key);
            Interlocked.Increment(ref _localCacheHits);
            return await _l1Cache.GetExpirationAsync(key, cancellationToken).AnyContext();
        }

        _logger.LogTrace("Local cache miss: {Key}", key);
        return await _l2Cache.GetExpirationAsync(key, cancellationToken).AnyContext();
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

        var cacheValue = await _l1Cache.GetSetAsync<T>(key, pageIndex, pageSize, cancellationToken).AnyContext();
        if (cacheValue.HasValue)
        {
            _logger.LogTrace("Local cache hit: {Key}", key);
            Interlocked.Increment(ref _localCacheHits);
            return cacheValue;
        }

        _logger.LogTrace("Local cache miss: {Key}", key);
        cacheValue = await _l2Cache.GetSetAsync<T>(key, pageIndex, pageSize, cancellationToken).AnyContext();

        if (cacheValue.HasValue)
        {
            var expiration = await _l2Cache.GetExpirationAsync(key, cancellationToken).AnyContext();
            var localExpiration = _GetLocalExpiration(expiration);
            _logger.LogTrace("Setting local cache key: {Key} with expiration: {Expiration}", key, localExpiration);
            // Use UpsertAsync to replace any existing L1 data (not SetAddAsync which would merge)
            await _l1Cache.UpsertAsync(key, cacheValue.Value!, localExpiration, cancellationToken).AnyContext();
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
            await RemoveAsync(key, cancellationToken).AnyContext();
            return false;
        }

        _logger.LogTrace("Setting key {Key} with expiration: {Expiration}", key, expiration);

        var updated = await _l2Cache.UpsertAsync(key, value, expiration, cancellationToken).AnyContext();

        if (updated)
        {
            var localExpiration = _GetLocalExpiration(expiration);
            await _l1Cache.UpsertAsync(key, value, localExpiration, cancellationToken).AnyContext();
        }
        else
        {
            // Remove from local cache when upsert fails
            await _l1Cache.RemoveAsync(key, cancellationToken).AnyContext();
        }

        await _PublishInvalidationAsync(
                new CacheInvalidationMessage { InstanceId = _instanceId, Key = key },
                cancellationToken
            )
            .AnyContext();

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
            await RemoveAllAsync(value.Keys, cancellationToken).AnyContext();
            return 0;
        }

        _logger.LogTrace("Setting keys {Keys} with expiration: {Expiration}", value.Keys, expiration);

        var setCount = await _l2Cache.UpsertAllAsync(value, expiration, cancellationToken).AnyContext();

        if (setCount == value.Count)
        {
            var localExpiration = _GetLocalExpiration(expiration);
            await _l1Cache.UpsertAllAsync(value, localExpiration, cancellationToken).AnyContext();
        }
        else
        {
            // Remove all keys from local cache when set fails or partially succeeds
            await _l1Cache.RemoveAllAsync(value.Keys, cancellationToken).AnyContext();
        }

        await _PublishInvalidationAsync(
                new CacheInvalidationMessage { InstanceId = _instanceId, Keys = value.Keys.ToArray() },
                cancellationToken
            )
            .AnyContext();

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
            await RemoveAsync(key, cancellationToken).AnyContext();
            return false;
        }

        _logger.LogTrace("Adding key {Key} to local cache with expiration: {Expiration}", key, expiration);

        var added = await _l2Cache.TryInsertAsync(key, value, expiration, cancellationToken).AnyContext();

        if (added)
        {
            var localExpiration = _GetLocalExpiration(expiration);
            await _l1Cache.UpsertAsync(key, value, localExpiration, cancellationToken).AnyContext();
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
            await RemoveAsync(key, cancellationToken).AnyContext();
            return false;
        }

        var replaced = await _l2Cache.TryReplaceAsync(key, value, expiration, cancellationToken).AnyContext();

        if (replaced)
        {
            var localExpiration = _GetLocalExpiration(expiration);
            await _l1Cache.UpsertAsync(key, value, localExpiration, cancellationToken).AnyContext();
        }
        else
        {
            // Remove from local cache when replace fails
            await _l1Cache.RemoveAsync(key, cancellationToken).AnyContext();
        }

        await _PublishInvalidationAsync(
                new CacheInvalidationMessage { InstanceId = _instanceId, Key = key },
                cancellationToken
            )
            .AnyContext();

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
            await RemoveAsync(key, cancellationToken).AnyContext();
            return false;
        }

        var replaced = await _l2Cache
            .TryReplaceIfEqualAsync(key, expected, value, expiration, cancellationToken)
            .AnyContext();

        if (replaced)
        {
            // Use UpsertAsync instead of TryReplaceIfEqualAsync for local cache because we know
            // the distributed cache now has this exact value, and we need local cache to be in sync
            var localExpiration = _GetLocalExpiration(expiration);
            await _l1Cache.UpsertAsync(key, value, localExpiration, cancellationToken).AnyContext();
        }
        else
        {
            // Remove from local cache when replace fails
            await _l1Cache.RemoveAsync(key, cancellationToken).AnyContext();
        }

        await _PublishInvalidationAsync(
                new CacheInvalidationMessage { InstanceId = _instanceId, Key = key },
                cancellationToken
            )
            .AnyContext();

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
            await RemoveAsync(key, cancellationToken).AnyContext();
            return 0;
        }

        var newValue = await _l2Cache.IncrementAsync(key, amount, expiration, cancellationToken).AnyContext();

        if (newValue == 0)
        {
            // When result is 0, remove from local cache (conservative approach)
            await _l1Cache.RemoveAsync(key, cancellationToken).AnyContext();
        }
        else
        {
            var localExpiration = _GetLocalExpiration(expiration);
            await _l1Cache.UpsertAsync(key, newValue, localExpiration, cancellationToken).AnyContext();
        }

        await _PublishInvalidationAsync(
                new CacheInvalidationMessage { InstanceId = _instanceId, Key = key },
                cancellationToken
            )
            .AnyContext();

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
            await RemoveAsync(key, cancellationToken).AnyContext();
            return 0;
        }

        var newValue = await _l2Cache.IncrementAsync(key, amount, expiration, cancellationToken).AnyContext();

        if (newValue == 0)
        {
            // When result is 0, remove from local cache (conservative approach)
            await _l1Cache.RemoveAsync(key, cancellationToken).AnyContext();
        }
        else
        {
            var localExpiration = _GetLocalExpiration(expiration);
            await _l1Cache.UpsertAsync(key, newValue, localExpiration, cancellationToken).AnyContext();
        }

        await _PublishInvalidationAsync(
                new CacheInvalidationMessage { InstanceId = _instanceId, Key = key },
                cancellationToken
            )
            .AnyContext();

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
            await RemoveAsync(key, cancellationToken).AnyContext();
            return 0;
        }

        var difference = await _l2Cache.SetIfHigherAsync(key, value, expiration, cancellationToken).AnyContext();

        if (Math.Abs(difference) > double.Epsilon)
        {
            // Value was updated - we know the new value is exactly what we passed in
            var localExpiration = _GetLocalExpiration(expiration);
            await _l1Cache.UpsertAsync(key, value, localExpiration, cancellationToken).AnyContext();
        }
        else
        {
            // Value was not updated - remove from local cache since we don't know actual value
            await _l1Cache.RemoveAsync(key, cancellationToken).AnyContext();
        }

        await _PublishInvalidationAsync(
                new CacheInvalidationMessage { InstanceId = _instanceId, Key = key },
                cancellationToken
            )
            .AnyContext();

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
            await RemoveAsync(key, cancellationToken).AnyContext();
            return 0;
        }

        var difference = await _l2Cache.SetIfHigherAsync(key, value, expiration, cancellationToken).AnyContext();

        if (difference != 0)
        {
            var localExpiration = _GetLocalExpiration(expiration);
            await _l1Cache.UpsertAsync(key, value, localExpiration, cancellationToken).AnyContext();
        }
        else
        {
            await _l1Cache.RemoveAsync(key, cancellationToken).AnyContext();
        }

        await _PublishInvalidationAsync(
                new CacheInvalidationMessage { InstanceId = _instanceId, Key = key },
                cancellationToken
            )
            .AnyContext();

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
            await RemoveAsync(key, cancellationToken).AnyContext();
            return 0;
        }

        var difference = await _l2Cache.SetIfLowerAsync(key, value, expiration, cancellationToken).AnyContext();

        if (Math.Abs(difference) > double.Epsilon)
        {
            var localExpiration = _GetLocalExpiration(expiration);
            await _l1Cache.UpsertAsync(key, value, localExpiration, cancellationToken).AnyContext();
        }
        else
        {
            await _l1Cache.RemoveAsync(key, cancellationToken).AnyContext();
        }

        await _PublishInvalidationAsync(
                new CacheInvalidationMessage { InstanceId = _instanceId, Key = key },
                cancellationToken
            )
            .AnyContext();

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
            await RemoveAsync(key, cancellationToken).AnyContext();
            return 0;
        }

        var difference = await _l2Cache.SetIfLowerAsync(key, value, expiration, cancellationToken).AnyContext();

        if (difference != 0)
        {
            var localExpiration = _GetLocalExpiration(expiration);
            await _l1Cache.UpsertAsync(key, value, localExpiration, cancellationToken).AnyContext();
        }
        else
        {
            await _l1Cache.RemoveAsync(key, cancellationToken).AnyContext();
        }

        await _PublishInvalidationAsync(
                new CacheInvalidationMessage { InstanceId = _instanceId, Key = key },
                cancellationToken
            )
            .AnyContext();

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
        var addedCount = await _l2Cache.SetAddAsync(key, items, expiration, cancellationToken).AnyContext();

        if (addedCount == items.Length)
        {
            var localExpiration = _GetLocalExpiration(expiration);
            await _l1Cache.SetAddAsync(key, items, localExpiration, cancellationToken).AnyContext();
        }
        else
        {
            // Partial success - remove to force re-fetch
            await _l1Cache.RemoveAsync(key, cancellationToken).AnyContext();
        }

        await _PublishInvalidationAsync(
                new CacheInvalidationMessage { InstanceId = _instanceId, Key = key },
                cancellationToken
            )
            .AnyContext();

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

        var removed = await _l2Cache.RemoveAsync(key, cancellationToken).AnyContext();
        await _l1Cache.RemoveAsync(key, cancellationToken).AnyContext();

        // Only notify other nodes if the key actually existed and was removed
        if (removed)
        {
            await _PublishInvalidationAsync(
                    new CacheInvalidationMessage { InstanceId = _instanceId, Key = key },
                    cancellationToken
                )
                .AnyContext();
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

        var removed = await _l2Cache.RemoveIfEqualAsync(key, expected, cancellationToken).AnyContext();

        // Always remove from local cache unconditionally (local cache might have stale value)
        await _l1Cache.RemoveAsync(key, cancellationToken).AnyContext();

        // Only notify other nodes if the key was actually removed from distributed cache
        if (removed)
        {
            await _PublishInvalidationAsync(
                    new CacheInvalidationMessage { InstanceId = _instanceId, Key = key },
                    cancellationToken
                )
                .AnyContext();
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

        var removed = await _l2Cache.RemoveAllAsync(items!, cancellationToken).AnyContext();
        await _l1Cache.RemoveAllAsync(items!, cancellationToken).AnyContext();

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
                .AnyContext();
        }

        return removed;
    }

    /// <inheritdoc />
    public async ValueTask<int> RemoveByPrefixAsync(string prefix, CancellationToken cancellationToken = default)
    {
        _ThrowIfDisposed();
        Argument.IsNotNull(prefix);
        cancellationToken.ThrowIfCancellationRequested();

        var removed = await _l2Cache.RemoveByPrefixAsync(prefix, cancellationToken).AnyContext();
        await _l1Cache.RemoveByPrefixAsync(prefix, cancellationToken).AnyContext();

        // Only notify other nodes if keys were actually removed
        if (removed > 0)
        {
            await _PublishInvalidationAsync(
                    new CacheInvalidationMessage { InstanceId = _instanceId, Prefix = prefix },
                    cancellationToken
                )
                .AnyContext();
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
        var removedCount = await _l2Cache.SetRemoveAsync(key, items, expiration, cancellationToken).AnyContext();

        if (removedCount == items.Length)
        {
            await _l1Cache.SetRemoveAsync(key, items, expiration, cancellationToken).AnyContext();
        }
        else
        {
            // Partial success - remove to force re-fetch
            await _l1Cache.RemoveAsync(key, cancellationToken).AnyContext();
        }

        // Only notify other nodes if items were actually removed
        if (removedCount > 0)
        {
            await _PublishInvalidationAsync(
                    new CacheInvalidationMessage { InstanceId = _instanceId, Key = key },
                    cancellationToken
                )
                .AnyContext();
        }

        return removedCount;
    }

    /// <inheritdoc />
    public async ValueTask FlushAsync(CancellationToken cancellationToken = default)
    {
        _ThrowIfDisposed();
        cancellationToken.ThrowIfCancellationRequested();

        await _l2Cache.FlushAsync(cancellationToken).AnyContext();
        await _l1Cache.FlushAsync(cancellationToken).AnyContext();
        await _PublishInvalidationAsync(
                new CacheInvalidationMessage { InstanceId = _instanceId, FlushAll = true },
                cancellationToken
            )
            .AnyContext();
    }

    #endregion

    #region Private Helpers

    private async ValueTask _PublishInvalidationAsync(CacheInvalidationMessage message, CancellationToken ct)
    {
        try
        {
            await _publisher.PublishAsync(message, ct).AnyContext();
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Publish failure is non-fatal: other instances may have stale L1 data
            // until their TTL expires. This is acceptable for eventual consistency.
            _logger.LogWarning(
                ex,
                "Failed to publish cache invalidation (keyCount={KeyCount}, hasPrefix={HasPrefix}, flushAll={FlushAll}), other instances may serve stale data until TTL expires",
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

    private TimeSpan? _GetLocalExpiration(TimeSpan? expiration) => _options.DefaultLocalExpiration ?? expiration;

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
