// Copyright (c) Mahmoud Shaheen, 2024. All rights reserved

using EasyCaching.Core;

namespace Framework.Caching.EasyCache;

internal sealed class MemoryEasyCachingCache(IEasyCachingProvider easyCache, string keyPrefix) : ICache
{
    #region Set

    public async Task SetAsync<T>(
        string cacheKey,
        T cacheValue,
        TimeSpan expiration,
        CancellationToken cancellationToken = default
    )
    {
        cacheKey = keyPrefix + cacheKey;

        await easyCache.SetAsync(cacheKey, cacheValue, expiration, cancellationToken);
    }

    public async Task<bool> TrySetAsync<T>(
        string cacheKey,
        T cacheValue,
        TimeSpan expiration,
        CancellationToken cancellationToken = default
    )
    {
        cacheKey = keyPrefix + cacheKey;

        return await easyCache.TrySetAsync(cacheKey, cacheValue, expiration, cancellationToken);
    }

    public async Task SetAllAsync<T>(
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

    public async Task<CacheValue<T>> GetAsync<T>(string cacheKey, CancellationToken cancellationToken = default)
    {
        cacheKey = keyPrefix + cacheKey;

        var result = await easyCache.GetAsync<T>(cacheKey, cancellationToken);

        return new(result.Value, result.HasValue);
    }

    public async Task<IDictionary<string, CacheValue<T>>> GetAllAsync<T>(
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

    public async Task<IDictionary<string, CacheValue<T>>> GetByPrefixAsync<T>(
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

    public async Task<IEnumerable<string>> GetAllKeysByPrefixAsync(
        string prefix,
        CancellationToken cancellationToken = default
    )
    {
        prefix = keyPrefix + prefix;

        return await easyCache.GetAllKeysByPrefixAsync(prefix, cancellationToken);
    }

    public async Task<bool> ExistsAsync(string cacheKey, CancellationToken cancellationToken = default)
    {
        cacheKey = keyPrefix + cacheKey;

        return await easyCache.ExistsAsync(cacheKey, cancellationToken);
    }

    public Task<TimeSpan> GetExpirationAsync(string cacheKey, CancellationToken cancellationToken = default)
    {
        cacheKey = keyPrefix + cacheKey;

        return easyCache.GetExpirationAsync(cacheKey, cancellationToken);
    }

    public async Task<int> GetCountAsync(string prefix = "", CancellationToken cancellationToken = default)
    {
        prefix = keyPrefix + prefix;

        return await easyCache.GetCountAsync(prefix, cancellationToken);
    }

    #endregion

    #region Remove

    public async Task<bool> RemoveAsync(string cacheKey, CancellationToken cancellationToken = default)
    {
        cacheKey = keyPrefix + cacheKey;

        await easyCache.RemoveAsync(cacheKey, cancellationToken);
    }

    public async Task<int> RemoveAllAsync(IEnumerable<string> cacheKeys, CancellationToken cancellationToken = default)
    {
        cacheKeys = cacheKeys.Select(x => keyPrefix + x);

        await easyCache.RemoveAllAsync(cacheKeys, cancellationToken);
    }

    public async Task<int> RemoveByPrefixAsync(string prefix, CancellationToken cancellationToken = default)
    {
        prefix = keyPrefix + prefix;

        await easyCache.RemoveByPrefixAsync(prefix, cancellationToken);
    }

    public async Task<int> RemoveByPatternAsync(string pattern, CancellationToken cancellationToken = default)
    {
        pattern = keyPrefix + pattern;

        await easyCache.RemoveByPatternAsync(pattern, cancellationToken);
    }

    public async Task FlushAsync(CancellationToken cancellationToken = default)
    {
        await easyCache.FlushAsync(cancellationToken);
    }

    #endregion
}
