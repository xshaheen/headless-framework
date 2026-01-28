// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Caching;

namespace Headless.Caching;

public static class CacheExtensions
{
    public static async Task<CacheValue<T>> GetOrAddAsync<T>(
        this ICache cache,
        string key,
        Func<Task<T?>> factory,
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
        await cache.UpsertAsync(key, value, expiration, cancellationToken);

        return new(value, hasValue: true);
    }
}
