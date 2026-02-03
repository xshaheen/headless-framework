// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Framework.Checks;
using Framework.Threading;

namespace Framework.Caching;

/// <summary>
/// Extension methods for <see cref="ICache{T}"/> with cache stampede protection.
/// </summary>
[PublicAPI]
public static class CacheOfTExtensions
{
    /// <summary>
    /// Gets a value from the cache, or adds it using the factory if not present.
    /// Provides cache stampede protection - concurrent requests for the same key
    /// will wait for the first factory to complete rather than all executing.
    /// </summary>
    /// <typeparam name="T">The type of the cached value.</typeparam>
    /// <param name="cache">The cache instance.</param>
    /// <param name="key">The cache key.</param>
    /// <param name="factory">
    /// The factory function to create the value if not found in cache.
    /// Receives the cancellation token for proper cancellation propagation.
    /// </param>
    /// <param name="expiration">The expiration time for the cached value.</param>
    /// <param name="cancellationToken">Optional cancellation token.</param>
    /// <returns>
    /// A <see cref="CacheValue{T}"/> containing the cached or newly created value.
    /// </returns>
    /// <remarks>
    /// <para>
    /// This method implements the double-check pattern for cache stampede protection:
    /// </para>
    /// <list type="number">
    /// <item>Check cache (fast path, no lock)</item>
    /// <item>Acquire per-key lock</item>
    /// <item>Check cache again (another request may have populated it)</item>
    /// <item>Execute factory and cache result</item>
    /// </list>
    /// </remarks>
    /// <example>
    /// <code>
    /// var result = await cache.GetOrAddAsync(
    ///     "user:123",
    ///     async ct => await _userRepository.GetByIdAsync(123, ct),
    ///     TimeSpan.FromMinutes(5),
    ///     cancellationToken
    /// );
    /// </code>
    /// </example>
    public static async Task<CacheValue<T>> GetOrAddAsync<T>(
        this ICache<T> cache,
        string key,
        Func<CancellationToken, Task<T?>> factory,
        TimeSpan expiration,
        CancellationToken cancellationToken = default
    )
    {
        Argument.IsNotNullOrEmpty(key);
        Argument.IsNotNull(factory);

        // First check - avoid lock acquisition if already cached
        var cacheValue = await cache.GetAsync(key, cancellationToken).AnyContext();

        if (cacheValue.HasValue)
        {
            return cacheValue;
        }

        // Acquire per-key lock for stampede protection
        using (await KeyedLock.LockAsync(key, cancellationToken).AnyContext())
        {
            // Double-check after acquiring lock
            cacheValue = await cache.GetAsync(key, cancellationToken).AnyContext();

            if (cacheValue.HasValue)
            {
                return cacheValue;
            }

            // Execute factory and cache result
            var value = await factory(cancellationToken).AnyContext();
            await cache.UpsertAsync(key, value, expiration, cancellationToken).AnyContext();

            return new CacheValue<T>(value, hasValue: true);
        }
    }
}
