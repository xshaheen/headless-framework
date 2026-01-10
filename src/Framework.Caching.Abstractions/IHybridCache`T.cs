// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Framework.Caching;

/// <summary>
/// A typed wrapper around <see cref="IHybridCache"/> for a specific type.
/// Useful for dependency injection when you want to inject a cache for a specific type.
/// </summary>
/// <typeparam name="T">The type of values stored in this cache.</typeparam>
[PublicAPI]
public interface IHybridCache<T>
{
    /// <summary>Gets a value from the cache.</summary>
    /// <param name="key">The cache key.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A <see cref="CacheValue{T}"/> indicating whether the value exists.</returns>
    Task<CacheValue<T>> GetAsync(string key, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets a value from the cache or creates it using the provided factory.
    /// </summary>
    /// <param name="key">The cache key.</param>
    /// <param name="factory">The factory function to create the value.</param>
    /// <param name="options">Optional entry-specific options.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The cached or newly created value.</returns>
    Task<CacheValue<T>> GetOrSetAsync(
        string key,
        Func<CancellationToken, Task<T?>> factory,
        HybridCacheEntryOptions? options = null,
        CancellationToken cancellationToken = default
    );

    /// <summary>Sets a value in the cache.</summary>
    /// <param name="key">The cache key.</param>
    /// <param name="value">The value to cache.</param>
    /// <param name="options">Optional entry-specific options.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task SetAsync(
        string key,
        T? value,
        HybridCacheEntryOptions? options = null,
        CancellationToken cancellationToken = default
    );

    /// <summary>Removes a value from the cache.</summary>
    /// <param name="key">The cache key.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task RemoveAsync(string key, CancellationToken cancellationToken = default);

    /// <summary>Checks if a key exists in the cache.</summary>
    /// <param name="key">The cache key.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns><see langword="true"/> if the key exists.</returns>
    Task<bool> ExistsAsync(string key, CancellationToken cancellationToken = default);

    /// <summary>Expires a cache entry, keeping it for fail-safe scenarios.</summary>
    /// <param name="key">The cache key.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task ExpireAsync(string key, CancellationToken cancellationToken = default);
}

/// <summary>
/// Default implementation of <see cref="IHybridCache{T}"/> that delegates to <see cref="IHybridCache"/>.
/// </summary>
/// <typeparam name="T">The type of values stored in this cache.</typeparam>
[PublicAPI]
public sealed class HybridCache<T>(IHybridCache cache) : IHybridCache<T>
{
    public Task<CacheValue<T>> GetAsync(string key, CancellationToken cancellationToken = default)
    {
        return cache.GetAsync<T>(key, cancellationToken);
    }

    public Task<CacheValue<T>> GetOrSetAsync(
        string key,
        Func<CancellationToken, Task<T?>> factory,
        HybridCacheEntryOptions? options = null,
        CancellationToken cancellationToken = default
    )
    {
        return cache.GetOrSetAsync(key, factory, options, cancellationToken);
    }

    public Task SetAsync(
        string key,
        T? value,
        HybridCacheEntryOptions? options = null,
        CancellationToken cancellationToken = default
    )
    {
        return cache.SetAsync(key, value, options, cancellationToken);
    }

    public Task RemoveAsync(string key, CancellationToken cancellationToken = default)
    {
        return cache.RemoveAsync(key, cancellationToken);
    }

    public Task<bool> ExistsAsync(string key, CancellationToken cancellationToken = default)
    {
        return cache.ExistsAsync(key, cancellationToken);
    }

    public Task ExpireAsync(string key, CancellationToken cancellationToken = default)
    {
        return cache.ExpireAsync(key, cancellationToken);
    }
}
