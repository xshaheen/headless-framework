// Copyright (c) Mahmoud Shaheen, 2024. All rights reserved

using EasyCaching.Core;

namespace Framework.Caching.EasyCache;

internal sealed class MemoryEasyCachingCache(IEasyCachingProvider easyCache, string keyPrefix) : ICache
{
    #region Set

    public async ValueTask SetAsync<T>(
        string cacheKey,
        T cacheValue,
        TimeSpan expiration,
        CancellationToken cancellationToken = default
    )
    {
        cacheKey = keyPrefix + cacheKey;

        await easyCache.SetAsync(cacheKey, cacheValue, expiration, cancellationToken);
    }

    public async ValueTask<bool> TrySetAsync<T>(
        string cacheKey,
        T cacheValue,
        TimeSpan expiration,
        CancellationToken cancellationToken = default
    )
    {
        cacheKey = keyPrefix + cacheKey;

        return await easyCache.TrySetAsync(cacheKey, cacheValue, expiration, cancellationToken);
    }

    public async ValueTask SetAllAsync<T>(
        IDictionary<string, T> value,
        TimeSpan expiration,
        CancellationToken cancellationToken = default
    )
    {
        value = value.ToDictionary(x => keyPrefix + x.Key, x => x.Value, StringComparer.Ordinal);

        await easyCache.SetAllAsync(value, expiration, cancellationToken);
    }

    #endregion

    #region Get

    public async ValueTask<CacheValue<T>> GetAsync<T>(string cacheKey, CancellationToken cancellationToken = default)
    {
        cacheKey = keyPrefix + cacheKey;

        var result = await easyCache.GetAsync<T>(cacheKey, cancellationToken);

        return new(result.Value, result.HasValue);
    }

    public async ValueTask<Dictionary<string, CacheValue<T>>> GetAllAsync<T>(
        IEnumerable<string> cacheKeys,
        CancellationToken cancellationToken = default
    )
    {
        cacheKeys = cacheKeys.Select(x => keyPrefix + x);

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
        prefix = keyPrefix + prefix;

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
        prefix = keyPrefix + prefix;

        return await easyCache.GetAllKeysByPrefixAsync(prefix, cancellationToken);
    }

    public async ValueTask<bool> ExistsAsync(string cacheKey, CancellationToken cancellationToken = default)
    {
        cacheKey = keyPrefix + cacheKey;

        return await easyCache.ExistsAsync(cacheKey, cancellationToken);
    }

    public Task<TimeSpan> GetExpirationAsync(string cacheKey, CancellationToken cancellationToken = default)
    {
        cacheKey = keyPrefix + cacheKey;

        return easyCache.GetExpirationAsync(cacheKey, cancellationToken);
    }

    public async ValueTask<int> GetCountAsync(string prefix = "", CancellationToken cancellationToken = default)
    {
        prefix = keyPrefix + prefix;

        return await easyCache.GetCountAsync(prefix, cancellationToken);
    }

    #endregion

    #region Remove

    public async ValueTask RemoveAsync(string cacheKey, CancellationToken cancellationToken = default)
    {
        cacheKey = keyPrefix + cacheKey;

        await easyCache.RemoveAsync(cacheKey, cancellationToken);
    }

    public async ValueTask RemoveAllAsync(IEnumerable<string> cacheKeys, CancellationToken cancellationToken = default)
    {
        cacheKeys = cacheKeys.Select(x => keyPrefix + x);

        await easyCache.RemoveAllAsync(cacheKeys, cancellationToken);
    }

    public async ValueTask RemoveByPrefixAsync(string prefix, CancellationToken cancellationToken = default)
    {
        prefix = keyPrefix + prefix;

        await easyCache.RemoveByPrefixAsync(prefix, cancellationToken);
    }

    public async ValueTask RemoveByPatternAsync(string pattern, CancellationToken cancellationToken = default)
    {
        pattern = keyPrefix + pattern;

        await easyCache.RemoveByPatternAsync(pattern, cancellationToken);
    }

    public async ValueTask FlushAsync(CancellationToken cancellationToken = default)
    {
        await easyCache.FlushAsync(cancellationToken);
    }

    #endregion
}
