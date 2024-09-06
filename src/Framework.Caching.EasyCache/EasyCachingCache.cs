using EasyCaching.Core;

namespace Framework.Caching.EasyCache;

internal sealed class EasyCachingCache(IEasyCachingProvider easyCache) : ICache
{
    #region Set

    public async ValueTask SetAsync<T>(
        string cacheKey,
        T cacheValue,
        TimeSpan expiration,
        CancellationToken cancellationToken = default
    )
    {
        await easyCache.SetAsync(cacheKey, cacheValue, expiration, cancellationToken);
    }

    public async ValueTask<bool> TrySetAsync<T>(
        string cacheKey,
        T cacheValue,
        TimeSpan expiration,
        CancellationToken cancellationToken = default
    )
    {
        return await easyCache.TrySetAsync(cacheKey, cacheValue, expiration, cancellationToken);
    }

    public async ValueTask SetAllAsync<T>(
        IDictionary<string, T> value,
        TimeSpan expiration,
        CancellationToken cancellationToken = default
    )
    {
        await easyCache.SetAllAsync(value, expiration, cancellationToken);
    }

    #endregion

    #region Get

    public async ValueTask<CacheValue<T>> GetAsync<T>(string cacheKey, CancellationToken cancellationToken = default)
    {
        var result = await easyCache.GetAsync<T>(cacheKey, cancellationToken);

        return new(result.Value, result.HasValue);
    }

    public async ValueTask<Dictionary<string, CacheValue<T>>> GetAllAsync<T>(
        IEnumerable<string> cacheKeys,
        CancellationToken cancellationToken = default
    )
    {
        var result = await easyCache.GetAllAsync<T>(cacheKeys, cancellationToken);

        return result.ToDictionary(
            pair => pair.Key,
            pair => new CacheValue<T>(pair.Value.Value, pair.Value.HasValue),
            StringComparer.Ordinal
        );
    }

    public async ValueTask<Dictionary<string, CacheValue<T>>> GetByPrefixAsync<T>(
        string prefix,
        CancellationToken cancellationToken = default
    )
    {
        var result = await easyCache.GetByPrefixAsync<T>(prefix, cancellationToken);

        return result.ToDictionary(
            pair => pair.Key,
            pair => new CacheValue<T>(pair.Value.Value, pair.Value.HasValue),
            StringComparer.Ordinal
        );
    }

    public async ValueTask<IEnumerable<string>> GetAllKeysByPrefixAsync(
        string prefix,
        CancellationToken cancellationToken = default
    )
    {
        return await easyCache.GetAllKeysByPrefixAsync(prefix, cancellationToken);
    }

    public async ValueTask<bool> ExistsAsync(string cacheKey, CancellationToken cancellationToken = default)
    {
        return await easyCache.ExistsAsync(cacheKey, cancellationToken);
    }

    public Task<TimeSpan> GetExpirationAsync(string cacheKey, CancellationToken cancellationToken = default)
    {
        return easyCache.GetExpirationAsync(cacheKey, cancellationToken);
    }

    public async ValueTask<int> GetCountAsync(string prefix = "", CancellationToken cancellationToken = default)
    {
        return await easyCache.GetCountAsync(prefix, cancellationToken);
    }

    #endregion

    #region Remove

    public async ValueTask RemoveAsync(string cacheKey, CancellationToken cancellationToken = default)
    {
        await easyCache.RemoveAsync(cacheKey, cancellationToken);
    }

    public async ValueTask RemoveAllAsync(IEnumerable<string> cacheKeys, CancellationToken cancellationToken = default)
    {
        await easyCache.RemoveAllAsync(cacheKeys, cancellationToken);
    }

    public async ValueTask RemoveByPrefixAsync(string prefix, CancellationToken cancellationToken = default)
    {
        await easyCache.RemoveByPrefixAsync(prefix, cancellationToken);
    }

    public async ValueTask RemoveByPatternAsync(string pattern, CancellationToken cancellationToken = default)
    {
        await easyCache.RemoveByPatternAsync(pattern, cancellationToken);
    }

    public async ValueTask FlushAsync(CancellationToken cancellationToken = default)
    {
        await easyCache.FlushAsync(cancellationToken);
    }

    #endregion
}
