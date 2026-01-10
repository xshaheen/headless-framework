// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Framework.Caching;

/// <summary>
/// A high-level cache interface supporting cache-aside pattern with fail-safe and stampede protection.
/// Unlike <see cref="ICache"/> which provides low-level operations, this interface focuses on
/// the GetOrSet pattern with advanced features like fail-safe fallback and eager refresh.
/// </summary>
[PublicAPI]
public interface IHybridCache : IDisposable
{
    /// <summary>Gets a value from the cache.</summary>
    /// <typeparam name="T">The type of the cached value.</typeparam>
    /// <param name="key">The cache key.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A <see cref="CacheValue{T}"/> indicating whether the value exists and its value if present.</returns>
    Task<CacheValue<T>> GetAsync<T>(string key, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets a value from the cache or creates it using the provided factory if not present.
    /// This method provides stampede protection - only one factory execution per key.
    /// </summary>
    /// <typeparam name="T">The type of the cached value.</typeparam>
    /// <param name="key">The cache key.</param>
    /// <param name="factory">The factory function to create the value if not cached.</param>
    /// <param name="options">Optional entry-specific options.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The cached or newly created value.</returns>
    Task<CacheValue<T>> GetOrSetAsync<T>(
        string key,
        Func<CancellationToken, Task<T?>> factory,
        HybridCacheEntryOptions? options = null,
        CancellationToken cancellationToken = default
    );

    /// <summary>Sets a value in the cache.</summary>
    /// <typeparam name="T">The type of the value.</typeparam>
    /// <param name="key">The cache key.</param>
    /// <param name="value">The value to cache.</param>
    /// <param name="options">Optional entry-specific options.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task SetAsync<T>(
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
    /// <returns><see langword="true"/> if the key exists; otherwise, <see langword="false"/>.</returns>
    Task<bool> ExistsAsync(string key, CancellationToken cancellationToken = default);

    /// <summary>
    /// Expires a cache entry, marking it as stale but keeping it for fail-safe scenarios.
    /// Different from <see cref="RemoveAsync"/> which completely removes the entry.
    /// </summary>
    /// <param name="key">The cache key.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task ExpireAsync(string key, CancellationToken cancellationToken = default);
}
