// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;

namespace Headless.Caching;

/// <summary>
/// Provides <see cref="ICache"/> extension methods that use the cache instance
/// <see cref="ICache.DefaultEntryOptions"/> instead of per-call <see cref="CacheEntryOptions"/>.
/// </summary>
[PublicAPI]
public static class CacheDefaultEntryExtensions
{
    /// <summary>
    /// Gets a value from the cache, or creates and stores it using the factory when missing, applying the cache instance
    /// <see cref="ICache.DefaultEntryOptions"/>.
    /// </summary>
    /// <typeparam name="T">The type of the cached value.</typeparam>
    /// <param name="cache">The cache instance.</param>
    /// <param name="key">The cache key.</param>
    /// <param name="factory">The factory function to create the value when not found in the cache.</param>
    /// <param name="cancellationToken">A token to monitor for cancellation requests.</param>
    /// <returns>The cached or newly created value wrapped in <see cref="CacheValue{T}"/>.</returns>
    /// <exception cref="InvalidOperationException">
    /// <see cref="ICache.DefaultEntryOptions"/> is <see langword="null"/> for the cache instance.
    /// </exception>
    public static ValueTask<CacheValue<T>> GetOrAddAsync<T>(
        this ICache cache,
        string key,
        Func<CancellationToken, ValueTask<T?>> factory,
        CancellationToken cancellationToken = default
    )
    {
        Argument.IsNotNull(cache);

        return cache.GetOrAddAsync(
            key,
            factory,
            _GetRequiredDefaultEntryOptions(cache.DefaultEntryOptions, cache.GetType().Name),
            cancellationToken
        );
    }

    /// <summary>
    /// Gets a value from the cache, or refreshes it using a conditional factory when missing or expired, applying
    /// the cache instance <see cref="ICache.DefaultEntryOptions"/>.
    /// </summary>
    /// <typeparam name="T">The type of the cached value.</typeparam>
    /// <param name="cache">The cache instance.</param>
    /// <param name="key">The cache key.</param>
    /// <param name="factory">The conditional factory invoked on a miss or refresh.</param>
    /// <param name="cancellationToken">A token to monitor for cancellation requests.</param>
    /// <returns>The cached, extended, or newly created value wrapped in <see cref="CacheValue{T}"/>.</returns>
    /// <exception cref="InvalidOperationException">
    /// <see cref="ICache.DefaultEntryOptions"/> is <see langword="null"/> for the cache instance.
    /// </exception>
    public static ValueTask<CacheValue<T>> GetOrAddAsync<T>(
        this ICache cache,
        string key,
        Func<CacheFactoryContext<T>, CancellationToken, ValueTask<CacheFactoryResult<T>>> factory,
        CancellationToken cancellationToken = default
    )
    {
        Argument.IsNotNull(cache);

        return cache.GetOrAddAsync(
            key,
            factory,
            _GetRequiredDefaultEntryOptions(cache.DefaultEntryOptions, cache.GetType().Name),
            cancellationToken
        );
    }

    /// <summary>
    /// Gets a value from the cache, or creates and stores it using the factory when missing, applying the typed cache
    /// instance <see cref="ICache{T}.DefaultEntryOptions"/>.
    /// </summary>
    /// <typeparam name="T">The type of the cached value.</typeparam>
    /// <param name="cache">The typed cache instance.</param>
    /// <param name="key">The cache key.</param>
    /// <param name="factory">The factory function to create the value when not found in the cache.</param>
    /// <param name="cancellationToken">A token to monitor for cancellation requests.</param>
    /// <returns>The cached or newly created value wrapped in <see cref="CacheValue{T}"/>.</returns>
    /// <exception cref="InvalidOperationException">
    /// <see cref="ICache{T}.DefaultEntryOptions"/> is <see langword="null"/> for the cache instance.
    /// </exception>
    public static ValueTask<CacheValue<T>> GetOrAddAsync<T>(
        this ICache<T> cache,
        string key,
        Func<CancellationToken, ValueTask<T?>> factory,
        CancellationToken cancellationToken = default
    )
    {
        Argument.IsNotNull(cache);

        return cache.GetOrAddAsync(
            key,
            factory,
            _GetRequiredDefaultEntryOptions(cache.DefaultEntryOptions, cache.GetType().Name),
            cancellationToken
        );
    }

    /// <summary>
    /// Gets a value from the cache, or refreshes it using a conditional factory when missing or expired, applying
    /// the typed cache instance <see cref="ICache{T}.DefaultEntryOptions"/>.
    /// </summary>
    /// <typeparam name="T">The type of the cached value.</typeparam>
    /// <param name="cache">The typed cache instance.</param>
    /// <param name="key">The cache key.</param>
    /// <param name="factory">The conditional factory invoked on a miss or refresh.</param>
    /// <param name="cancellationToken">A token to monitor for cancellation requests.</param>
    /// <returns>The cached, extended, or newly created value wrapped in <see cref="CacheValue{T}"/>.</returns>
    /// <exception cref="InvalidOperationException">
    /// <see cref="ICache{T}.DefaultEntryOptions"/> is <see langword="null"/> for the cache instance.
    /// </exception>
    public static ValueTask<CacheValue<T>> GetOrAddAsync<T>(
        this ICache<T> cache,
        string key,
        Func<CacheFactoryContext<T>, CancellationToken, ValueTask<CacheFactoryResult<T>>> factory,
        CancellationToken cancellationToken = default
    )
    {
        Argument.IsNotNull(cache);

        return cache.GetOrAddAsync(
            key,
            factory,
            _GetRequiredDefaultEntryOptions(cache.DefaultEntryOptions, cache.GetType().Name),
            cancellationToken
        );
    }

    private static CacheEntryOptions _GetRequiredDefaultEntryOptions(
        CacheEntryOptions? defaultEntryOptions,
        string typeName
    )
    {
        return defaultEntryOptions
            ?? throw new InvalidOperationException(
                $"The cache instance ({typeName}) has no {nameof(ICache.DefaultEntryOptions)} configured, "
                    + "so the GetOrAddAsync overloads without CacheEntryOptions cannot be used. Configure the default at "
                    + "registration (for example: options.DefaultEntryOptions = new CacheEntryOptions { Duration = TimeSpan.FromMinutes(5) }) "
                    + "or call the GetOrAddAsync overload that takes CacheEntryOptions explicitly."
            );
    }
}
