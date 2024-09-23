// Copyright (c) Mahmoud Shaheen, 2024. All rights reserved

namespace Framework.Caching;

public static class CacheExtensions
{
    public static async Task<CacheValue<T>> GetOrAddAsync<T>(
        this ICache cache,
        string key,
        Func<Task<T>> factory,
        TimeSpan expiration,
        CancellationToken cancellationToken = default
    )
    {
        var cacheValue = await cache.GetAsync<T>(key, cancellationToken);

        if (cacheValue.HasValue)
        {
            return cacheValue;
        }

        var value = await factory();
        await cache.UpsertAsync(key, cacheValue, expiration, cancellationToken);

        return new(value, hasValue: true);
    }
}
