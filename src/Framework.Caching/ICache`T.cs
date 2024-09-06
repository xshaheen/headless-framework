namespace Framework.Caching;

public interface ICache<T>
{
    #region Set

    /// <summary>Sets the specified cacheKey, cacheValue and expiration.</summary>
    ValueTask SetAsync(
        string cacheKey,
        T cacheValue,
        TimeSpan expiration,
        CancellationToken cancellationToken = default
    );

    /// <summary>Tries the set.</summary>
    /// <returns><see langword="true"/>, if set was tried, <see langword="false"/> otherwise.</returns>
    ValueTask<bool> TrySetAsync(
        string cacheKey,
        T cacheValue,
        TimeSpan expiration,
        CancellationToken cancellationToken = default
    );

    /// <summary>Sets all async.</summary>
    ValueTask SetAllAsync(
        IDictionary<string, T> value,
        TimeSpan expiration,
        CancellationToken cancellationToken = default
    );

    #endregion

    #region Get

    /// <summary>Gets the specified cache key.</summary>
    ValueTask<CacheValue<T>> GetAsync(string cacheKey, CancellationToken cancellationToken = default);

    /// <summary>Gets all.</summary>
    ValueTask<Dictionary<string, CacheValue<T>>> GetAllAsync(
        IEnumerable<string> cacheKeys,
        CancellationToken cancellationToken = default
    );

    /// <summary>Gets the by prefix.</summary>
    ValueTask<Dictionary<string, CacheValue<T>>> GetByPrefixAsync(
        string prefix,
        CancellationToken cancellationToken = default
    );

    #endregion

    #region Remove

    /// <summary>Remove the specified cache key.</summary>
    ValueTask RemoveAsync(string cacheKey, CancellationToken cancellationToken = default);

    #endregion
}

public sealed class Cache<T>(ICache cache) : ICache<T>
{
    public ValueTask SetAsync(
        string cacheKey,
        T cacheValue,
        TimeSpan expiration,
        CancellationToken cancellationToken = default
    )
    {
        return cache.SetAsync(cacheKey, cacheValue, expiration, cancellationToken);
    }

    public ValueTask<bool> TrySetAsync(
        string cacheKey,
        T cacheValue,
        TimeSpan expiration,
        CancellationToken cancellationToken = default
    )
    {
        return cache.TrySetAsync(cacheKey, cacheValue, expiration, cancellationToken);
    }

    public ValueTask SetAllAsync(
        IDictionary<string, T> value,
        TimeSpan expiration,
        CancellationToken cancellationToken = default
    )
    {
        return cache.SetAllAsync(value, expiration, cancellationToken);
    }

    public ValueTask<CacheValue<T>> GetAsync(string cacheKey, CancellationToken cancellationToken = default)
    {
        return cache.GetAsync<T>(cacheKey, cancellationToken);
    }

    public ValueTask<Dictionary<string, CacheValue<T>>> GetAllAsync(
        IEnumerable<string> cacheKeys,
        CancellationToken cancellationToken = default
    )
    {
        return cache.GetAllAsync<T>(cacheKeys, cancellationToken);
    }

    public ValueTask<Dictionary<string, CacheValue<T>>> GetByPrefixAsync(
        string prefix,
        CancellationToken cancellationToken = default
    )
    {
        return cache.GetByPrefixAsync<T>(prefix, cancellationToken);
    }

    public ValueTask RemoveAsync(string cacheKey, CancellationToken cancellationToken = default)
    {
        return cache.RemoveAsync(cacheKey, cancellationToken);
    }
}
