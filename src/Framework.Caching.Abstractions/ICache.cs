// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Framework.Checks;
using Framework.Threading;

namespace Framework.Caching;

[PublicAPI]
public interface ICache
{
    #region Update

    /// <summary>Sets the specified cacheKey, cacheValue and expiration.</summary>
    Task<bool> UpsertAsync<T>(
        string key,
        T? value,
        TimeSpan? expiration,
        CancellationToken cancellationToken = default
    );

    /// <summary>Upsert all async.</summary>
    Task<int> UpsertAllAsync<T>(
        IDictionary<string, T> value,
        TimeSpan? expiration,
        CancellationToken cancellationToken = default
    );

    /// <summary>Tries the add.</summary>
    /// <returns><see langword="true"/>, if set/add success, <see langword="false"/> if <paramref name="key"/> already exists.</returns>
    Task<bool> TryInsertAsync<T>(
        string key,
        T? value,
        TimeSpan? expiration,
        CancellationToken cancellationToken = default
    );

    Task<bool> TryReplaceAsync<T>(
        string key,
        T? value,
        TimeSpan? expiration,
        CancellationToken cancellationToken = default
    );

    Task<bool> TryReplaceIfEqualAsync<T>(
        string key,
        T? expected,
        T? value,
        TimeSpan? expiration,
        CancellationToken cancellationToken = default
    );

    Task<double> IncrementAsync(
        string key,
        double amount,
        TimeSpan? expiration,
        CancellationToken cancellationToken = default
    );

    Task<long> IncrementAsync(
        string key,
        long amount,
        TimeSpan? expiration,
        CancellationToken cancellationToken = default
    );

    Task<double> SetIfHigherAsync(
        string key,
        double value,
        TimeSpan? expiration,
        CancellationToken cancellationToken = default
    );

    Task<long> SetIfHigherAsync(
        string key,
        long value,
        TimeSpan? expiration,
        CancellationToken cancellationToken = default
    );

    Task<double> SetIfLowerAsync(
        string key,
        double value,
        TimeSpan? expiration,
        CancellationToken cancellationToken = default
    );

    Task<long> SetIfLowerAsync(
        string key,
        long value,
        TimeSpan? expiration,
        CancellationToken cancellationToken = default
    );

    Task<long> SetAddAsync<T>(
        string key,
        IEnumerable<T> value,
        TimeSpan? expiration,
        CancellationToken cancellationToken = default
    );

    #endregion

    #region GetOrAdd

    /// <summary>
    /// Gets a value from the cache, or adds it using the factory if not present.
    /// Provides cache stampede protection - concurrent requests for the same key
    /// will wait for the first factory to complete rather than all executing.
    /// </summary>
    /// <typeparam name="T">The type of the cached value.</typeparam>
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
    async Task<CacheValue<T>> GetOrAddAsync<T>(
        string key,
        Func<CancellationToken, Task<T?>> factory,
        TimeSpan expiration,
        CancellationToken cancellationToken = default
    )
    {
        Argument.IsNotNullOrEmpty(key);
        Argument.IsNotNull(factory);

        // First check - avoid lock acquisition if already cached
        var cacheValue = await GetAsync<T>(key, cancellationToken).AnyContext();

        if (cacheValue.HasValue)
        {
            return cacheValue;
        }

        // Acquire per-key lock for stampede protection
        using (await KeyedLock.LockAsync(key, cancellationToken).AnyContext())
        {
            // Double-check after acquiring lock - another request may have populated cache
            cacheValue = await GetAsync<T>(key, cancellationToken).AnyContext();

            if (cacheValue.HasValue)
            {
                return cacheValue;
            }

            // Execute factory and cache result
            var value = await factory(cancellationToken).AnyContext();
            await UpsertAsync(key, value, expiration, cancellationToken).AnyContext();

            return new CacheValue<T>(value, hasValue: true);
        }
    }

    #endregion

    #region Get

    /// <summary>Gets all.</summary>
    Task<IDictionary<string, CacheValue<T>>> GetAllAsync<T>(
        IEnumerable<string> cacheKeys,
        CancellationToken cancellationToken = default
    );

    /// <summary>Gets the by prefix.</summary>
    Task<IDictionary<string, CacheValue<T>>> GetByPrefixAsync<T>(
        string prefix,
        CancellationToken cancellationToken = default
    );

    /// <summary>Gets all keys by prefix.</summary>
    Task<IReadOnlyList<string>> GetAllKeysByPrefixAsync(string prefix, CancellationToken cancellationToken = default);

    /// <summary>Gets the specified cache key.</summary>
    Task<CacheValue<T>> GetAsync<T>(string key, CancellationToken cancellationToken = default);

    /// <summary>Gets the count async.</summary>
    Task<int> GetCountAsync(string prefix = "", CancellationToken cancellationToken = default);

    /// <summary>Check if the key exists in the cache.</summary>
    Task<bool> ExistsAsync(string key, CancellationToken cancellationToken = default);

    /// <summary>Gets the expiration of specify cache key.</summary>
    Task<TimeSpan?> GetExpirationAsync(string key, CancellationToken cancellationToken = default);

    Task<CacheValue<ICollection<T>>> GetSetAsync<T>(
        string key,
        int? pageIndex = null,
        int pageSize = 100,
        CancellationToken cancellationToken = default
    );

    #endregion

    #region Remove

    /// <summary>Remove the specified cache key.</summary>
    Task<bool> RemoveAsync(string key, CancellationToken cancellationToken = default);

    /// <summary>Remove only if equal the expected value.</summary>
    Task<bool> RemoveIfEqualAsync<T>(string key, T? expected, CancellationToken cancellationToken = default);

    /// <summary>Removes all.</summary>
    Task<int> RemoveAllAsync(IEnumerable<string> cacheKeys, CancellationToken cancellationToken = default);

    /// <summary>Removes cached item by cache key's prefix.</summary>
    Task<int> RemoveByPrefixAsync(string prefix, CancellationToken cancellationToken = default);

    /// <summary>Remove some values from set.</summary>
    Task<long> SetRemoveAsync<T>(
        string key,
        IEnumerable<T> value,
        TimeSpan? expiration,
        CancellationToken cancellationToken = default
    );

    /// <summary>Flush all cached item.</summary>
    Task FlushAsync(CancellationToken cancellationToken = default);

    #endregion
}

public interface IInMemoryCache : ICache;

public interface IDistributedCache : ICache;
