namespace Framework.Caching;

public interface ICache
{
    #region Set

    /// <summary>Sets the specified cacheKey, cacheValue and expiration.</summary>
    ValueTask SetAsync<T>(
        string cacheKey,
        T cacheValue,
        TimeSpan expiration,
        CancellationToken cancellationToken = default
    );

    /// <summary>Tries the set.</summary>
    /// <returns><see langword="true"/>, if set was tried, <see langword="false"/> otherwise.</returns>
    ValueTask<bool> TrySetAsync<T>(
        string cacheKey,
        T cacheValue,
        TimeSpan expiration,
        CancellationToken cancellationToken = default
    );

    /// <summary>Sets all async.</summary>
    ValueTask SetAllAsync<T>(
        IDictionary<string, T> value,
        TimeSpan expiration,
        CancellationToken cancellationToken = default
    );

    #endregion

    #region Get

    /// <summary>Gets all.</summary>
    ValueTask<Dictionary<string, CacheValue<T>>> GetAllAsync<T>(
        IEnumerable<string> cacheKeys,
        CancellationToken cancellationToken = default
    );

    /// <summary>Gets the by prefix.</summary>
    ValueTask<Dictionary<string, CacheValue<T>>> GetByPrefixAsync<T>(
        string prefix,
        CancellationToken cancellationToken = default
    );

    /// <summary>Gets all keys by prefix.</summary>
    ValueTask<IEnumerable<string>> GetAllKeysByPrefixAsync(
        string prefix,
        CancellationToken cancellationToken = default
    );

    /// <summary>Gets the specified cache key.</summary>
    ValueTask<CacheValue<T>> GetAsync<T>(string cacheKey, CancellationToken cancellationToken = default);

    /// <summary>Gets the count async.</summary>
    ValueTask<int> GetCountAsync(string prefix = "", CancellationToken cancellationToken = default);

    /// <summary>Check if the key exists in the cache.</summary>
    ValueTask<bool> ExistsAsync(string cacheKey, CancellationToken cancellationToken = default);

    /// <summary>Gets the expiration of specify cache key.</summary>
    Task<TimeSpan> GetExpirationAsync(string cacheKey, CancellationToken cancellationToken = default);

    #endregion

    #region Remove

    /// <summary>Remove the specified cache key.</summary>
    ValueTask RemoveAsync(string cacheKey, CancellationToken cancellationToken = default);

    /// <summary>Removes all.</summary>
    ValueTask RemoveAllAsync(IEnumerable<string> cacheKeys, CancellationToken cancellationToken = default);

    /// <summary>Removes cached item by cache key's prefix.</summary>
    ValueTask RemoveByPrefixAsync(string prefix, CancellationToken cancellationToken = default);

    /// <summary>Removes cached items by a cache key pattern.</summary>
    ValueTask RemoveByPatternAsync(string pattern, CancellationToken cancellationToken = default);

    /// <summary>Flush all cached item.</summary>
    ValueTask FlushAsync(CancellationToken cancellationToken = default);

    #endregion
}
