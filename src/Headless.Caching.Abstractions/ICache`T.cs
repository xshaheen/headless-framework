// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Caching;

public interface ICache<T>
{
    /// <summary>
    /// Gets a value from cache, or creates it using the factory if not found.
    /// Uses keyed locking to prevent cache stampedes (multiple concurrent factory executions for the same key).
    /// </summary>
    /// <param name="key">The cache key.</param>
    /// <param name="factory">The factory function to create the value if not found in cache. Receives the cancellation token.</param>
    /// <param name="expiration">Expiration time for the cached value.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The cached or newly created value wrapped in <see cref="CacheValue{T}"/>.</returns>
    ValueTask<CacheValue<T>> GetOrAddAsync(
        string key,
        Func<CancellationToken, ValueTask<T?>> factory,
        TimeSpan expiration,
        CancellationToken cancellationToken = default
    );

    #region Update

    ValueTask<bool> UpsertAsync(
        string cacheKey,
        T? cacheValue,
        TimeSpan expiration,
        CancellationToken cancellationToken = default
    );

    ValueTask<int> UpsertAllAsync(
        IDictionary<string, T> value,
        TimeSpan expiration,
        CancellationToken cancellationToken = default
    );

    ValueTask<bool> TryAddAsync(
        string cacheKey,
        T? cacheValue,
        TimeSpan expiration,
        CancellationToken cancellationToken = default
    );

    ValueTask<bool> TryReplaceAsync(
        string key,
        T? value,
        TimeSpan expiration,
        CancellationToken cancellationToken = default
    );

    ValueTask<bool> TryReplaceIfEqualAsync(
        string key,
        T? value,
        T? expected,
        TimeSpan expiration,
        CancellationToken cancellationToken = default
    );

    ValueTask<long> SetAddAsync(
        string key,
        IEnumerable<T> value,
        TimeSpan expiration,
        CancellationToken cancellationToken = default
    );

    #endregion

    #region Get

    ValueTask<IDictionary<string, CacheValue<T>>> GetAllAsync(
        IEnumerable<string> cacheKeys,
        CancellationToken cancellationToken = default
    );

    ValueTask<IDictionary<string, CacheValue<T>>> GetByPrefixAsync(
        string prefix,
        CancellationToken cancellationToken = default
    );

    ValueTask<CacheValue<T>> GetAsync(string cacheKey, CancellationToken cancellationToken = default);

    ValueTask<CacheValue<ICollection<T>>> GetSetAsync(string key, int? pageIndex = null, int pageSize = 100);

    #endregion

    #region Remove

    ValueTask<bool> RemoveAsync(string cacheKey, CancellationToken cancellationToken = default);

    ValueTask<bool> RemoveIfEqualAsync(string cacheKey, T? expected);

    ValueTask<int> RemoveByPrefixAsync(string prefix, CancellationToken cancellationToken = default);

    ValueTask<long> SetRemoveAsync(
        string key,
        IEnumerable<T> value,
        TimeSpan? expiration,
        CancellationToken cancellationToken = default
    );

    #endregion
}

public class Cache<T>(ICache cache) : ICache<T>
{
    public ValueTask<CacheValue<T>> GetOrAddAsync(
        string key,
        Func<CancellationToken, ValueTask<T?>> factory,
        TimeSpan expiration,
        CancellationToken cancellationToken = default
    )
    {
        return cache.GetOrAddAsync(key, factory, expiration, cancellationToken);
    }

    public ValueTask<bool> UpsertAsync(
        string cacheKey,
        T? cacheValue,
        TimeSpan expiration,
        CancellationToken cancellationToken = default
    )
    {
        return cache.UpsertAsync(cacheKey, cacheValue, expiration, cancellationToken);
    }

    public ValueTask<int> UpsertAllAsync(
        IDictionary<string, T> value,
        TimeSpan expiration,
        CancellationToken cancellationToken = default
    )
    {
        return cache.UpsertAllAsync(value, expiration, cancellationToken);
    }

    public ValueTask<bool> TryAddAsync(
        string cacheKey,
        T? cacheValue,
        TimeSpan expiration,
        CancellationToken cancellationToken = default
    )
    {
        return cache.TryInsertAsync(cacheKey, cacheValue, expiration, cancellationToken);
    }

    public ValueTask<bool> TryReplaceAsync(
        string key,
        T? value,
        TimeSpan expiration,
        CancellationToken cancellationToken = default
    )
    {
        return cache.TryReplaceAsync(key, value, expiration, cancellationToken);
    }

    public ValueTask<bool> TryReplaceIfEqualAsync(
        string key,
        T? value,
        T? expected,
        TimeSpan expiration,
        CancellationToken cancellationToken = default
    )
    {
        return cache.TryReplaceIfEqualAsync(key, expected, value, expiration, cancellationToken);
    }

    public ValueTask<long> SetAddAsync(
        string key,
        IEnumerable<T> value,
        TimeSpan expiration,
        CancellationToken cancellationToken = default
    )
    {
        return cache.SetAddAsync(key, value, expiration, cancellationToken);
    }

    public ValueTask<IDictionary<string, CacheValue<T>>> GetAllAsync(
        IEnumerable<string> cacheKeys,
        CancellationToken cancellationToken = default
    )
    {
        return cache.GetAllAsync<T>(cacheKeys, cancellationToken);
    }

    public ValueTask<IDictionary<string, CacheValue<T>>> GetByPrefixAsync(
        string prefix,
        CancellationToken cancellationToken = default
    )
    {
        return cache.GetByPrefixAsync<T>(prefix, cancellationToken);
    }

    public ValueTask<CacheValue<T>> GetAsync(string cacheKey, CancellationToken cancellationToken = default)
    {
        return cache.GetAsync<T>(cacheKey, cancellationToken);
    }

    public ValueTask<CacheValue<ICollection<T>>> GetSetAsync(string key, int? pageIndex = null, int pageSize = 100)
    {
        return cache.GetSetAsync<T>(key, pageIndex, pageSize);
    }

    public ValueTask<bool> RemoveAsync(string cacheKey, CancellationToken cancellationToken = default)
    {
        return cache.RemoveAsync(cacheKey, cancellationToken);
    }

    public ValueTask<bool> RemoveIfEqualAsync(string cacheKey, T? expected)
    {
        return cache.RemoveIfEqualAsync(cacheKey, expected);
    }

    public ValueTask<int> RemoveByPrefixAsync(string prefix, CancellationToken cancellationToken = default)
    {
        return cache.RemoveByPrefixAsync(prefix, cancellationToken);
    }

    public ValueTask<long> SetRemoveAsync(
        string key,
        IEnumerable<T> value,
        TimeSpan? expiration,
        CancellationToken cancellationToken = default
    )
    {
        return cache.SetRemoveAsync(key, value, expiration, cancellationToken);
    }
}

public interface IDistributedCache<T> : ICache<T>;

public interface IInMemoryCache<T> : ICache<T>;

public sealed class DistributedCache<T>(IDistributedCache cache) : Cache<T>(cache), IDistributedCache<T>;

public sealed class InMemoryCache<T>(IInMemoryCache cache) : Cache<T>(cache), IInMemoryCache<T>;
