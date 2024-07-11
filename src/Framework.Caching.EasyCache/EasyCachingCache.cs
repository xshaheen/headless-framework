using EasyCaching.Core;

namespace Framework.Caching.EasyCache;

internal sealed class EasyCachingCache(IEasyCachingProvider easyCacheProvider) : ICache
{
    public async ValueTask SetAsync<T>(
        string cacheKey,
        T cacheValue,
        TimeSpan expiration,
        CancellationToken cancellationToken = default
    )
    {
        await easyCacheProvider.SetAsync(cacheKey, cacheValue, expiration, cancellationToken);
    }

    public async ValueTask<bool> TrySetAsync<T>(
        string cacheKey,
        T cacheValue,
        TimeSpan expiration,
        CancellationToken cancellationToken = default
    )
    {
        return await easyCacheProvider.TrySetAsync(cacheKey, cacheValue, expiration, cancellationToken);
    }

    public async ValueTask SetAllAsync<T>(
        IDictionary<string, T> value,
        TimeSpan expiration,
        CancellationToken cancellationToken = default
    )
    {
        await easyCacheProvider.SetAllAsync(value, expiration, cancellationToken);
    }

    public async ValueTask RemoveAsync(string cacheKey, CancellationToken cancellationToken = default)
    {
        await easyCacheProvider.RemoveAsync(cacheKey, cancellationToken);
    }

    public async ValueTask RemoveAllAsync(IEnumerable<string> cacheKeys, CancellationToken cancellationToken = default)
    {
        await easyCacheProvider.RemoveAllAsync(cacheKeys, cancellationToken);
    }

    public async ValueTask RemoveByPrefixAsync(string prefix, CancellationToken cancellationToken = default)
    {
        await easyCacheProvider.RemoveByPrefixAsync(prefix, cancellationToken);
    }

    public async ValueTask RemoveByPatternAsync(string pattern, CancellationToken cancellationToken = default)
    {
        await easyCacheProvider.RemoveByPatternAsync(pattern, cancellationToken);
    }

    public async ValueTask<T> GetAsync<T>(string cacheKey, CancellationToken cancellationToken = default)
    {
        var result = await easyCacheProvider.GetAsync<T>(cacheKey, cancellationToken);

        return result.Value;
    }

    public async ValueTask<IDictionary<string, T>> GetAllAsync<T>(
        IEnumerable<string> cacheKeys,
        CancellationToken cancellationToken = default
    )
    {
        var result = await easyCacheProvider.GetAllAsync<T>(cacheKeys, cancellationToken);

        return result.ToDictionary(pair => pair.Key, pair => pair.Value.Value, StringComparer.Ordinal);
    }

    public async ValueTask<IDictionary<string, T>> GetByPrefixAsync<T>(
        string prefix,
        CancellationToken cancellationToken = default
    )
    {
        var result = await easyCacheProvider.GetByPrefixAsync<T>(prefix, cancellationToken);

        return result.ToDictionary(pair => pair.Key, pair => pair.Value.Value, StringComparer.Ordinal);
    }

    public async ValueTask<IEnumerable<string>> GetAllKeysByPrefixAsync(
        string prefix,
        CancellationToken cancellationToken = default
    )
    {
        return await easyCacheProvider.GetAllKeysByPrefixAsync(prefix, cancellationToken);
    }

    public async ValueTask<bool> ExistsAsync(string cacheKey, CancellationToken cancellationToken = default)
    {
        return await easyCacheProvider.ExistsAsync(cacheKey, cancellationToken);
    }

    public async ValueTask<int> GetCountAsync(string prefix = "", CancellationToken cancellationToken = default)
    {
        return await easyCacheProvider.GetCountAsync(prefix, cancellationToken);
    }

    public async ValueTask FlushAsync(CancellationToken cancellationToken = default)
    {
        await easyCacheProvider.FlushAsync(cancellationToken);
    }
}
