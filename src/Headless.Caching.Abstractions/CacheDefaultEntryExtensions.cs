// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;

namespace Headless.Caching;

/// <summary>
/// <see cref="ICache"/> extension overloads that use the cache instance's
/// <see cref="ICache.DefaultEntryOptions"/> instead of per-call <see cref="CacheEntryOptions"/>.
/// </summary>
[PublicAPI]
public static class CacheDefaultEntryExtensions
{
    /// <summary>
    /// Gets a value from cache, or creates it using the factory if not found, applying the cache instance's
    /// <see cref="ICache.DefaultEntryOptions"/>.
    /// </summary>
    /// <typeparam name="T">The type of the cached value.</typeparam>
    /// <param name="cache">The cache instance.</param>
    /// <param name="key">The cache key.</param>
    /// <param name="factory">The factory function to create the value if not found in cache.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The cached or newly created value wrapped in <see cref="CacheValue{T}"/>.</returns>
    /// <exception cref="InvalidOperationException">
    /// Thrown when <see cref="ICache.DefaultEntryOptions"/> is <see langword="null"/> for the cache instance.
    /// </exception>
    public static ValueTask<CacheValue<T>> GetOrAddAsync<T>(
        this ICache cache,
        string key,
        Func<CancellationToken, ValueTask<T?>> factory,
        CancellationToken cancellationToken = default
    )
    {
        Argument.IsNotNull(cache);

        return cache.GetOrAddAsync(key, factory, _GetRequiredDefaultEntryOptions(cache), cancellationToken);
    }

    /// <summary>
    /// Gets a value from cache, or refreshes it using a conditional factory (the HTTP-304 pattern), applying
    /// the cache instance's <see cref="ICache.DefaultEntryOptions"/>.
    /// </summary>
    /// <typeparam name="T">The type of the cached value.</typeparam>
    /// <param name="cache">The cache instance.</param>
    /// <param name="key">The cache key.</param>
    /// <param name="factory">The conditional factory invoked on a miss or refresh.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The cached, extended, or newly created value wrapped in <see cref="CacheValue{T}"/>.</returns>
    /// <exception cref="InvalidOperationException">
    /// Thrown when <see cref="ICache.DefaultEntryOptions"/> is <see langword="null"/> for the cache instance.
    /// </exception>
    public static ValueTask<CacheValue<T>> GetOrAddAsync<T>(
        this ICache cache,
        string key,
        Func<CacheFactoryContext<T>, CancellationToken, ValueTask<CacheFactoryResult<T>>> factory,
        CancellationToken cancellationToken = default
    )
    {
        Argument.IsNotNull(cache);

        return cache.GetOrAddAsync(key, factory, _GetRequiredDefaultEntryOptions(cache), cancellationToken);
    }

    /// <summary>
    /// Gets a value from cache, or creates it using the factory if not found, applying the typed cache
    /// instance's <see cref="ICache{T}.DefaultEntryOptions"/>.
    /// </summary>
    /// <typeparam name="T">The type of the cached value.</typeparam>
    /// <param name="cache">The typed cache instance.</param>
    /// <param name="key">The cache key.</param>
    /// <param name="factory">The factory function to create the value if not found in cache.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The cached or newly created value wrapped in <see cref="CacheValue{T}"/>.</returns>
    /// <exception cref="InvalidOperationException">
    /// Thrown when <see cref="ICache{T}.DefaultEntryOptions"/> is <see langword="null"/> for the cache instance.
    /// </exception>
    public static ValueTask<CacheValue<T>> GetOrAddAsync<T>(
        this ICache<T> cache,
        string key,
        Func<CancellationToken, ValueTask<T?>> factory,
        CancellationToken cancellationToken = default
    )
    {
        Argument.IsNotNull(cache);

        return cache.GetOrAddAsync(key, factory, _GetRequiredDefaultEntryOptions(cache), cancellationToken);
    }

    /// <summary>
    /// Gets a value from cache, or refreshes it using a conditional factory (the HTTP-304 pattern), applying
    /// the typed cache instance's <see cref="ICache{T}.DefaultEntryOptions"/>.
    /// </summary>
    /// <typeparam name="T">The type of the cached value.</typeparam>
    /// <param name="cache">The typed cache instance.</param>
    /// <param name="key">The cache key.</param>
    /// <param name="factory">The conditional factory invoked on a miss or refresh.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The cached, extended, or newly created value wrapped in <see cref="CacheValue{T}"/>.</returns>
    /// <exception cref="InvalidOperationException">
    /// Thrown when <see cref="ICache{T}.DefaultEntryOptions"/> is <see langword="null"/> for the cache instance.
    /// </exception>
    public static ValueTask<CacheValue<T>> GetOrAddAsync<T>(
        this ICache<T> cache,
        string key,
        Func<CacheFactoryContext<T>, CancellationToken, ValueTask<CacheFactoryResult<T>>> factory,
        CancellationToken cancellationToken = default
    )
    {
        Argument.IsNotNull(cache);

        return cache.GetOrAddAsync(key, factory, _GetRequiredDefaultEntryOptions(cache), cancellationToken);
    }

    private static CacheEntryOptions _GetRequiredDefaultEntryOptions(ICache cache)
    {
        return cache.DefaultEntryOptions
            ?? throw new InvalidOperationException(
                $"The cache instance ({cache.GetType().Name}) has no {nameof(ICache.DefaultEntryOptions)} configured, "
                    + "so the GetOrAddAsync overloads without CacheEntryOptions cannot be used. Configure the default at "
                    + "registration (for example: options.DefaultEntryOptions = new CacheEntryOptions { Duration = TimeSpan.FromMinutes(5) }) "
                    + "or call the GetOrAddAsync overload that takes CacheEntryOptions explicitly."
            );
    }

    private static CacheEntryOptions _GetRequiredDefaultEntryOptions<T>(ICache<T> cache)
    {
        return cache.DefaultEntryOptions
            ?? throw new InvalidOperationException(
                $"The cache instance ({cache.GetType().Name}) has no {nameof(ICache<T>.DefaultEntryOptions)} configured, "
                    + "so the GetOrAddAsync overloads without CacheEntryOptions cannot be used. Configure the default at "
                    + "registration (for example: options.DefaultEntryOptions = new CacheEntryOptions { Duration = TimeSpan.FromMinutes(5) }) "
                    + "or call the GetOrAddAsync overload that takes CacheEntryOptions explicitly."
            );
    }
}
