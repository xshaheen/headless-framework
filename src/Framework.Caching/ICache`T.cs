// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Framework.Caching;

public interface ICache<T>
{
    #region Update

    Task<bool> UpsertAsync(
        string cacheKey,
        T cacheValue,
        TimeSpan expiration,
        CancellationToken cancellationToken = default
    );

    Task<int> UpsertAllAsync(
        IDictionary<string, T> value,
        TimeSpan expiration,
        CancellationToken cancellationToken = default
    );

    Task<bool> TryAddAsync(
        string cacheKey,
        T cacheValue,
        TimeSpan expiration,
        CancellationToken cancellationToken = default
    );

    Task<bool> TryReplaceAsync(string key, T value, TimeSpan expiration, CancellationToken cancellationToken = default);

    Task<bool> TryReplaceIfEqualAsync(
        string key,
        T value,
        T expected,
        TimeSpan expiration,
        CancellationToken cancellationToken = default
    );

    Task<long> SetAddAsync(
        string key,
        IEnumerable<T> value,
        TimeSpan expiration,
        CancellationToken cancellationToken = default
    );

    #endregion

    #region Get

    Task<IDictionary<string, CacheValue<T>>> GetAllAsync(
        IEnumerable<string> cacheKeys,
        CancellationToken cancellationToken = default
    );

    Task<IDictionary<string, CacheValue<T>>> GetByPrefixAsync(
        string prefix,
        CancellationToken cancellationToken = default
    );

    Task<CacheValue<T>> GetAsync(string cacheKey, CancellationToken cancellationToken = default);

    Task<CacheValue<ICollection<T>>> GetSetAsync(string key, int? pageIndex = null, int pageSize = 100);

    #endregion

    #region Remove

    Task<bool> RemoveAsync(string cacheKey, CancellationToken cancellationToken = default);

    Task<bool> RemoveIfEqualAsync(string cacheKey, T expected);

    Task<int> RemoveByPrefixAsync(string prefix, CancellationToken cancellationToken = default);

    Task<long> SetRemoveAsync(
        string key,
        IEnumerable<T> value,
        TimeSpan expiration,
        CancellationToken cancellationToken = default
    );

    #endregion
}

public sealed class Cache<T>(ICache cache) : ICache<T>
{
    public Task<bool> UpsertAsync(
        string cacheKey,
        T cacheValue,
        TimeSpan expiration,
        CancellationToken cancellationToken = default
    )
    {
        return cache.UpsertAsync(cacheKey, cacheValue, expiration, cancellationToken);
    }

    public Task<int> UpsertAllAsync(
        IDictionary<string, T> value,
        TimeSpan expiration,
        CancellationToken cancellationToken = default
    )
    {
        return cache.UpsertAllAsync(value, expiration, cancellationToken);
    }

    public Task<bool> TryAddAsync(
        string cacheKey,
        T cacheValue,
        TimeSpan expiration,
        CancellationToken cancellationToken = default
    )
    {
        return cache.TryInsertAsync(cacheKey, cacheValue, expiration, cancellationToken);
    }

    public Task<bool> TryReplaceAsync(
        string key,
        T value,
        TimeSpan expiration,
        CancellationToken cancellationToken = default
    )
    {
        return cache.TryReplaceAsync(key, value, expiration, cancellationToken);
    }

    public Task<bool> TryReplaceIfEqualAsync(
        string key,
        T value,
        T expected,
        TimeSpan expiration,
        CancellationToken cancellationToken = default
    )
    {
        return cache.TryReplaceIfEqualAsync(key, expected, value, expiration, cancellationToken);
    }

    public Task<long> SetAddAsync(
        string key,
        IEnumerable<T> value,
        TimeSpan expiration,
        CancellationToken cancellationToken = default
    )
    {
        return cache.SetAddAsync(key, value, expiration, cancellationToken);
    }

    public Task<IDictionary<string, CacheValue<T>>> GetAllAsync(
        IEnumerable<string> cacheKeys,
        CancellationToken cancellationToken = default
    )
    {
        return cache.GetAllAsync<T>(cacheKeys, cancellationToken);
    }

    public Task<IDictionary<string, CacheValue<T>>> GetByPrefixAsync(
        string prefix,
        CancellationToken cancellationToken = default
    )
    {
        return cache.GetByPrefixAsync<T>(prefix, cancellationToken);
    }

    public Task<CacheValue<T>> GetAsync(string cacheKey, CancellationToken cancellationToken = default)
    {
        return cache.GetAsync<T>(cacheKey, cancellationToken);
    }

    public Task<CacheValue<ICollection<T>>> GetSetAsync(string key, int? pageIndex = null, int pageSize = 100)
    {
        return cache.GetSetAsync<T>(key, pageIndex, pageSize);
    }

    public Task<bool> RemoveAsync(string cacheKey, CancellationToken cancellationToken = default)
    {
        return cache.RemoveAsync(cacheKey, cancellationToken);
    }

    public Task<bool> RemoveIfEqualAsync(string cacheKey, T expected)
    {
        return cache.RemoveIfEqualAsync(cacheKey, expected);
    }

    public Task<int> RemoveByPrefixAsync(string prefix, CancellationToken cancellationToken = default)
    {
        return cache.RemoveByPrefixAsync(prefix, cancellationToken);
    }

    public Task<long> SetRemoveAsync(
        string key,
        IEnumerable<T> value,
        TimeSpan expiration,
        CancellationToken cancellationToken = default
    )
    {
        return cache.SetRemoveAsync(key, value, expiration, cancellationToken);
    }
}
