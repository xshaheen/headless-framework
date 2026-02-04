// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Threading;

namespace Headless.Caching;

/// <summary>
/// Extension methods for <see cref="ICache"/>.
/// </summary>
public static class CacheExtensions
{
    /// <summary>
    /// Gets a value from cache, or creates it using the factory if not found.
    /// Uses keyed locking to prevent cache stampedes (multiple concurrent factory executions for the same key).
    /// </summary>
    /// <typeparam name="T">The type of the cached value.</typeparam>
    /// <param name="cache">The cache instance.</param>
    /// <param name="key">The cache key.</param>
    /// <param name="factory">The factory function to create the value if not found in cache.</param>
    /// <param name="expiration">Expiration time for the cached value.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The cached or newly created value wrapped in <see cref="CacheValue{T}"/>.</returns>
    public static async ValueTask<CacheValue<T>> GetOrAddAsync<T>(
        this ICache cache,
        string key,
        Func<Task<T?>> factory,
        TimeSpan expiration,
        CancellationToken cancellationToken = default
    )
    {
        var cacheValue = await cache.GetAsync<T>(key, cancellationToken).AnyContext();

        if (cacheValue.HasValue)
        {
            return cacheValue;
        }

        using (await KeyedLock.LockAsync(key, cancellationToken).AnyContext())
        {
            // Double-check after acquiring lock
            cacheValue = await cache.GetAsync<T>(key, cancellationToken).AnyContext();
            if (cacheValue.HasValue)
            {
                return cacheValue;
            }

            var value = await factory().AnyContext();
            await cache.UpsertAsync(key, value, expiration, cancellationToken).AnyContext();

            return new(value, hasValue: true);
        }
    }
}
