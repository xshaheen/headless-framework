namespace Framework.Caching;

public interface ICache
{
    #region Update

    /// <summary>Sets the specified cacheKey, cacheValue and expiration.</summary>
    Task<bool> UpsertAsync<T>(string key, T value, TimeSpan? expiration, CancellationToken cancellationToken = default);

    /// <summary>Upsert all async.</summary>
    Task<int> UpsertAllAsync<T>(
        IDictionary<string, T> value,
        TimeSpan? expiration,
        CancellationToken cancellationToken = default
    );

    /// <summary>Tries the add.</summary>
    /// <returns><see langword="true"/>, if set/add success, <see langword="false"/> if <paramref name="key"/> already exists.</returns>
    Task<bool> TryInsertAsync<T>(
        string key,
        T value,
        TimeSpan? expiration,
        CancellationToken cancellationToken = default
    );

    Task<bool> TryReplaceAsync<T>(
        string key,
        T value,
        TimeSpan? expiration,
        CancellationToken cancellationToken = default
    );

    Task<bool> TryReplaceIfEqualAsync<T>(
        string key,
        T value,
        T expected,
        TimeSpan? expiration,
        CancellationToken cancellationToken = default
    );

    Task<double> IncrementAsync(
        string key,
        double amount,
        TimeSpan? expiration,
        CancellationToken cancellationToken = default
    );

    Task<long> IncrementAsync(
        string key,
        long amount,
        TimeSpan? expiration,
        CancellationToken cancellationToken = default
    );

    Task<double> SetIfHigherAsync(
        string key,
        double value,
        TimeSpan? expiration,
        CancellationToken cancellationToken = default
    );

    Task<long> SetIfHigherAsync(
        string key,
        long value,
        TimeSpan? expiration,
        CancellationToken cancellationToken = default
    );

    Task<double> SetIfLowerAsync(
        string key,
        double value,
        TimeSpan? expiration,
        CancellationToken cancellationToken = default
    );

    Task<long> SetIfLowerAsync(
        string key,
        long value,
        TimeSpan? expiration,
        CancellationToken cancellationToken = default
    );

    Task<long> SetAddAsync<T>(
        string key,
        IEnumerable<T> value,
        TimeSpan? expiration,
        CancellationToken cancellationToken = default
    );

    #endregion

    #region Get

    /// <summary>Gets all.</summary>
    Task<IDictionary<string, CacheValue<T>>> GetAllAsync<T>(
        IEnumerable<string> cacheKeys,
        CancellationToken cancellationToken = default
    );

    /// <summary>Gets the by prefix.</summary>
    Task<IDictionary<string, CacheValue<T>>> GetByPrefixAsync<T>(
        string prefix,
        CancellationToken cancellationToken = default
    );

    /// <summary>Gets all keys by prefix.</summary>
    Task<IEnumerable<string>> GetAllKeysByPrefixAsync(string prefix, CancellationToken cancellationToken = default);

    /// <summary>Gets the specified cache key.</summary>
    Task<CacheValue<T>> GetAsync<T>(string cacheKey, CancellationToken cancellationToken = default);

    /// <summary>Gets the count async.</summary>
    Task<int> GetCountAsync(string prefix = "", CancellationToken cancellationToken = default);

    /// <summary>Check if the key exists in the cache.</summary>
    Task<bool> ExistsAsync(string cacheKey, CancellationToken cancellationToken = default);

    /// <summary>Gets the expiration of specify cache key.</summary>
    Task<TimeSpan?> GetExpirationAsync(string cacheKey, CancellationToken cancellationToken = default);

    Task<CacheValue<ICollection<T>>> GetSetAsync<T>(string key, int? pageIndex = null, int pageSize = 100);

    #endregion

    #region Remove

    /// <summary>Remove the specified cache key.</summary>
    Task<bool> RemoveAsync(string cacheKey, CancellationToken cancellationToken = default);

    Task<bool> RemoveIfEqualAsync<T>(string cacheKey, T expected);

    /// <summary>Removes all.</summary>
    Task<int> RemoveAllAsync(IEnumerable<string> cacheKeys, CancellationToken cancellationToken = default);

    /// <summary>Removes cached item by cache key's prefix.</summary>
    Task<int> RemoveByPrefixAsync(string prefix, CancellationToken cancellationToken = default);

    Task<long> SetRemoveAsync<T>(
        string key,
        IEnumerable<T> value,
        TimeSpan expiration,
        CancellationToken cancellationToken = default
    );

    /// <summary>Flush all cached item.</summary>
    Task FlushAsync(CancellationToken cancellationToken = default);

    #endregion
}
