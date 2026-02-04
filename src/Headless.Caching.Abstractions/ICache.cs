// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Caching;

[PublicAPI]
public interface ICache
{
    /// <summary>
    /// Gets a value from cache, or creates it using the factory if not found.
    /// Uses keyed locking to prevent cache stampedes (multiple concurrent factory executions for the same key).
    /// </summary>
    /// <typeparam name="T">The type of the cached value.</typeparam>
    /// <param name="key">The cache key.</param>
    /// <param name="factory">The factory function to create the value if not found in cache. Receives the cancellation token.</param>
    /// <param name="expiration">Expiration time for the cached value.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The cached or newly created value wrapped in <see cref="CacheValue{T}"/>.</returns>
    ValueTask<CacheValue<T>> GetOrAddAsync<T>(
        string key,
        Func<CancellationToken, ValueTask<T?>> factory,
        TimeSpan expiration,
        CancellationToken cancellationToken = default
    );

    #region Update

    /// <summary>Sets the specified cacheKey, cacheValue and expiration.</summary>
    ValueTask<bool> UpsertAsync<T>(
        string key,
        T? value,
        TimeSpan? expiration,
        CancellationToken cancellationToken = default
    );

    /// <summary>Upsert all async.</summary>
    ValueTask<int> UpsertAllAsync<T>(
        IDictionary<string, T> value,
        TimeSpan? expiration,
        CancellationToken cancellationToken = default
    );

    /// <summary>Tries the add.</summary>
    /// <returns><see langword="true"/>, if set/add success, <see langword="false"/> if <paramref name="key"/> already exists.</returns>
    ValueTask<bool> TryInsertAsync<T>(
        string key,
        T? value,
        TimeSpan? expiration,
        CancellationToken cancellationToken = default
    );

    ValueTask<bool> TryReplaceAsync<T>(
        string key,
        T? value,
        TimeSpan? expiration,
        CancellationToken cancellationToken = default
    );

    ValueTask<bool> TryReplaceIfEqualAsync<T>(
        string key,
        T? expected,
        T? value,
        TimeSpan? expiration,
        CancellationToken cancellationToken = default
    );

    ValueTask<double> IncrementAsync(
        string key,
        double amount,
        TimeSpan? expiration,
        CancellationToken cancellationToken = default
    );

    ValueTask<long> IncrementAsync(
        string key,
        long amount,
        TimeSpan? expiration,
        CancellationToken cancellationToken = default
    );

    ValueTask<double> SetIfHigherAsync(
        string key,
        double value,
        TimeSpan? expiration,
        CancellationToken cancellationToken = default
    );

    ValueTask<long> SetIfHigherAsync(
        string key,
        long value,
        TimeSpan? expiration,
        CancellationToken cancellationToken = default
    );

    ValueTask<double> SetIfLowerAsync(
        string key,
        double value,
        TimeSpan? expiration,
        CancellationToken cancellationToken = default
    );

    ValueTask<long> SetIfLowerAsync(
        string key,
        long value,
        TimeSpan? expiration,
        CancellationToken cancellationToken = default
    );

    ValueTask<long> SetAddAsync<T>(
        string key,
        IEnumerable<T> value,
        TimeSpan? expiration,
        CancellationToken cancellationToken = default
    );

    #endregion

    #region Get

    /// <summary>Gets all.</summary>
    ValueTask<IDictionary<string, CacheValue<T>>> GetAllAsync<T>(
        IEnumerable<string> cacheKeys,
        CancellationToken cancellationToken = default
    );

    /// <summary>Gets the by prefix.</summary>
    ValueTask<IDictionary<string, CacheValue<T>>> GetByPrefixAsync<T>(
        string prefix,
        CancellationToken cancellationToken = default
    );

    /// <summary>Gets all keys by prefix.</summary>
    ValueTask<IReadOnlyList<string>> GetAllKeysByPrefixAsync(
        string prefix,
        CancellationToken cancellationToken = default
    );

    /// <summary>Gets the specified cache key.</summary>
    ValueTask<CacheValue<T>> GetAsync<T>(string key, CancellationToken cancellationToken = default);

    /// <summary>Gets the count async.</summary>
    ValueTask<int> GetCountAsync(string prefix = "", CancellationToken cancellationToken = default);

    /// <summary>Check if the key exists in the cache.</summary>
    ValueTask<bool> ExistsAsync(string key, CancellationToken cancellationToken = default);

    /// <summary>Gets the expiration of specify cache key.</summary>
    ValueTask<TimeSpan?> GetExpirationAsync(string key, CancellationToken cancellationToken = default);

    ValueTask<CacheValue<ICollection<T>>> GetSetAsync<T>(
        string key,
        int? pageIndex = null,
        int pageSize = 100,
        CancellationToken cancellationToken = default
    );

    #endregion

    #region Remove

    /// <summary>Remove the specified cache key.</summary>
    ValueTask<bool> RemoveAsync(string key, CancellationToken cancellationToken = default);

    /// <summary>Remove only if equal the expected value.</summary>
    ValueTask<bool> RemoveIfEqualAsync<T>(string key, T? expected, CancellationToken cancellationToken = default);

    /// <summary>Removes all.</summary>
    ValueTask<int> RemoveAllAsync(IEnumerable<string> cacheKeys, CancellationToken cancellationToken = default);

    /// <summary>Removes cached item by cache key's prefix.</summary>
    ValueTask<int> RemoveByPrefixAsync(string prefix, CancellationToken cancellationToken = default);

    /// <summary>Remove some values from set.</summary>
    ValueTask<long> SetRemoveAsync<T>(
        string key,
        IEnumerable<T> value,
        TimeSpan? expiration,
        CancellationToken cancellationToken = default
    );

    /// <summary>Flush all cached item.</summary>
    ValueTask FlushAsync(CancellationToken cancellationToken = default);

    #endregion
}

public interface IInMemoryCache : ICache;

public interface IDistributedCache : ICache;
