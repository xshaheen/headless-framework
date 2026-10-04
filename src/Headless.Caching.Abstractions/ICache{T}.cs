// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Caching;

/// <summary>
/// Defines a strongly typed projection of <see cref="ICache"/> that pins the value type to <typeparamref name="T"/>.
/// </summary>
/// <typeparam name="T">The type of values stored in and retrieved from this cache projection.</typeparam>
[PublicAPI]
public interface ICache<T>
{
    /// <summary>
    /// Gets the default <see cref="CacheEntryOptions"/> configured for this cache instance at registration.
    /// Used by overloads that omit options. When <see langword="null"/>, those overloads throw <see cref="InvalidOperationException"/>.
    /// </summary>
    CacheEntryOptions? DefaultEntryOptions { get; }

    /// <summary>
    /// Gets the in-process event surface for the underlying cache instance. The default implementation returns <see cref="CacheEvents.NoOp"/>.
    /// </summary>
    ICacheEvents Events => CacheEvents.NoOp;

    /// <summary>
    /// Gets a value from the cache, or creates and stores it using the factory when missing.
    /// Uses keyed locking to prevent concurrent factory executions for the same key.
    /// </summary>
    /// <param name="key">The cache key.</param>
    /// <param name="factory">The factory function to create the value when not found in the cache.</param>
    /// <param name="options">Cache entry options for the cached value.</param>
    /// <param name="cancellationToken">A token to monitor for cancellation requests.</param>
    /// <returns>The cached or newly created value wrapped in <see cref="CacheValue{T}"/>.</returns>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <see cref="CacheEntryOptions.Duration"/> is not positive, or fail-safe is enabled and
    /// <see cref="CacheEntryOptions.FailSafeMaxDuration"/> or <see cref="CacheEntryOptions.FailSafeThrottleDuration"/> is not positive.
    /// </exception>
    ValueTask<CacheValue<T>> GetOrAddAsync(
        string key,
        Func<CancellationToken, ValueTask<T?>> factory,
        CacheEntryOptions options,
        CancellationToken cancellationToken = default
    );

    /// <summary>
    /// Gets a value from the cache, or refreshes it using a conditional factory.
    /// </summary>
    /// <param name="key">The cache key.</param>
    /// <param name="factory">The conditional factory invoked on a miss or refresh.</param>
    /// <param name="options">Cache entry options for the cached value.</param>
    /// <param name="cancellationToken">A token to monitor for cancellation requests.</param>
    /// <returns>The cached, extended, or newly created value wrapped in <see cref="CacheValue{T}"/>.</returns>
    /// <exception cref="ArgumentOutOfRangeException">
    /// The entry options or an adaptive replacement set by the factory are invalid.
    /// </exception>
    ValueTask<CacheValue<T>> GetOrAddAsync(
        string key,
        Func<CacheFactoryContext<T>, CancellationToken, ValueTask<CacheFactoryResult<T>>> factory,
        CacheEntryOptions options,
        CancellationToken cancellationToken = default
    );

    #region Update

    /// <summary>Sets the value for <paramref name="cacheKey"/> with the specified <paramref name="expiration"/>.</summary>
    ValueTask<bool> UpsertAsync(
        string cacheKey,
        T? cacheValue,
        TimeSpan? expiration,
        CancellationToken cancellationToken = default
    );

    /// <summary>
    /// Sets a value directly while honoring <see cref="CacheEntryOptions"/> metadata.
    /// </summary>
    ValueTask<bool> UpsertEntryAsync(
        string cacheKey,
        T? cacheValue,
        CacheEntryOptions options,
        CancellationToken cancellationToken = default
    );

    /// <summary>Writes all entries in <paramref name="value"/>.</summary>
    ValueTask<int> UpsertAllAsync(
        IDictionary<string, T> value,
        TimeSpan? expiration,
        CancellationToken cancellationToken = default
    );

    /// <summary>Inserts <paramref name="cacheValue"/> only when <paramref name="cacheKey"/> does not exist.</summary>
    ValueTask<bool> TryInsertAsync(
        string cacheKey,
        T? cacheValue,
        TimeSpan? expiration,
        CancellationToken cancellationToken = default
    );

    /// <summary>Replaces the value only when <paramref name="key"/> exists.</summary>
    ValueTask<bool> TryReplaceAsync(
        string key,
        T? value,
        TimeSpan? expiration,
        CancellationToken cancellationToken = default
    );

    /// <summary>Atomically replaces the value only when the current stored value equals <paramref name="expected"/>.</summary>
    ValueTask<bool> TryReplaceIfEqualAsync(
        string key,
        T? expected,
        T? value,
        TimeSpan? expiration,
        CancellationToken cancellationToken = default
    );

    /// <summary>Adds members to the set stored at <paramref name="key"/>, creating the set when absent.</summary>
    ValueTask<long> SetAddAsync(
        string key,
        IEnumerable<T> value,
        TimeSpan? expiration,
        CancellationToken cancellationToken = default
    );

    #endregion

    #region Get

    /// <summary>Reads multiple keys in one call and returns a result envelope for each key.</summary>
    ValueTask<IDictionary<string, CacheValue<T>>> GetAllAsync(
        IEnumerable<string> cacheKeys,
        CancellationToken cancellationToken = default
    );

    /// <summary>Reads all fresh entries whose key starts with <paramref name="prefix"/>.</summary>
    ValueTask<IDictionary<string, CacheValue<T>>> GetByPrefixAsync(
        string prefix,
        CancellationToken cancellationToken = default
    );

    /// <summary>Gets the value for <paramref name="cacheKey"/>.</summary>
    ValueTask<CacheValue<T>> GetAsync(string cacheKey, CancellationToken cancellationToken = default);

    /// <summary>Reads a page of members from the set stored at <paramref name="key"/>.</summary>
    ValueTask<CacheValue<ICollection<T>>> GetSetAsync(
        string key,
        int? pageIndex = null,
        int pageSize = 100,
        CancellationToken cancellationToken = default
    );

    /// <summary>
    /// Extends the idle window of a sliding cache entry without materializing its value.
    /// </summary>
    /// <param name="cacheKey">The cache key.</param>
    /// <param name="cancellationToken">A token to monitor for cancellation requests.</param>
    ValueTask RefreshAsync(string cacheKey, CancellationToken cancellationToken = default);

    #endregion

    #region Remove

    /// <summary>Removes <paramref name="cacheKey"/> from the cache.</summary>
    ValueTask<bool> RemoveAsync(string cacheKey, CancellationToken cancellationToken = default);

    /// <summary>Logically expires an entry while preserving its fail-safe reserve.</summary>
    ValueTask<bool> ExpireAsync(string cacheKey, CancellationToken cancellationToken = default);

    /// <summary>Removes <paramref name="cacheKey"/> only when the current stored value equals <paramref name="expected"/>.</summary>
    ValueTask<bool> RemoveIfEqualAsync(string cacheKey, T? expected, CancellationToken cancellationToken = default);

    /// <summary>Removes all keys that start with <paramref name="prefix"/>.</summary>
    ValueTask<int> RemoveByPrefixAsync(string prefix, CancellationToken cancellationToken = default);

    /// <summary>Logically invalidates entries carrying <paramref name="tag"/>.</summary>
    ValueTask RemoveByTagAsync(string tag, CancellationToken cancellationToken = default);

    /// <summary>Logically clears the cache while preserving fail-safe reserves.</summary>
    ValueTask ClearAsync(CancellationToken cancellationToken = default);

    /// <summary>Removes the specified members from the set stored at <paramref name="key"/>.</summary>
    ValueTask<long> SetRemoveAsync(
        string key,
        IEnumerable<T> value,
        TimeSpan? expiration,
        CancellationToken cancellationToken = default
    );

    /// <summary>Removes all cached items for the specified cache keys.</summary>
    ValueTask<int> RemoveAllAsync(IEnumerable<string> cacheKeys, CancellationToken cancellationToken = default);

    /// <summary>
    /// Flushes the cache and removes all entries including fail-safe reserves.
    /// </summary>
    ValueTask FlushAsync(CancellationToken cancellationToken = default);

    #endregion

    #region Management

    /// <summary>Atomically adds <paramref name="amount"/> to the numeric value at <paramref name="key"/>, creating the key if absent.</summary>
    ValueTask<double> IncrementAsync(
        string key,
        double amount,
        TimeSpan? expiration,
        CancellationToken cancellationToken = default
    );

    /// <summary>Atomically adds <paramref name="amount"/> to the numeric value at <paramref name="key"/>, creating the key if absent.</summary>
    ValueTask<long> IncrementAsync(
        string key,
        long amount,
        TimeSpan? expiration,
        CancellationToken cancellationToken = default
    );

    /// <summary>Stores <paramref name="value"/> only when it is greater than the current stored value.</summary>
    ValueTask<double> SetIfHigherAsync(
        string key,
        double value,
        TimeSpan? expiration,
        CancellationToken cancellationToken = default
    );

    /// <summary>Stores <paramref name="value"/> only when it is greater than the current stored value.</summary>
    ValueTask<long> SetIfHigherAsync(
        string key,
        long value,
        TimeSpan? expiration,
        CancellationToken cancellationToken = default
    );

    /// <summary>Stores <paramref name="value"/> only when it is less than the current stored value.</summary>
    ValueTask<double> SetIfLowerAsync(
        string key,
        double value,
        TimeSpan? expiration,
        CancellationToken cancellationToken = default
    );

    /// <summary>Stores <paramref name="value"/> only when it is less than the current stored value.</summary>
    ValueTask<long> SetIfLowerAsync(
        string key,
        long value,
        TimeSpan? expiration,
        CancellationToken cancellationToken = default
    );

    /// <summary>Gets all keys that start with <paramref name="prefix"/>.</summary>
    ValueTask<IReadOnlyList<string>> GetAllKeysByPrefixAsync(
        string prefix,
        CancellationToken cancellationToken = default
    );

    /// <summary>Gets the count of cached items, optionally filtered by key prefix.</summary>
    ValueTask<long> GetCountAsync(string prefix = "", CancellationToken cancellationToken = default);

    /// <summary>Checks if the key exists in the cache.</summary>
    ValueTask<bool> ExistsAsync(string key, CancellationToken cancellationToken = default);

    /// <summary>Gets the remaining expiration of the specified cache key.</summary>
    ValueTask<TimeSpan?> GetExpirationAsync(string key, CancellationToken cancellationToken = default);

    #endregion
}

/// <summary>
/// Adapts an <see cref="ICache"/> instance to <see cref="ICache{T}"/> by forwarding calls with the type parameter fixed to <typeparamref name="T"/>.
/// </summary>
/// <typeparam name="T">The type of values stored and retrieved through this wrapper.</typeparam>
/// <param name="cache">The underlying cache instance.</param>
[PublicAPI]
public sealed class Cache<T>(ICache cache) : ICache<T>
{
    /// <inheritdoc />
    public CacheEntryOptions? DefaultEntryOptions => cache.DefaultEntryOptions;

    public ICacheEvents Events => cache.Events;

    public ValueTask<CacheValue<T>> GetOrAddAsync(
        string key,
        Func<CancellationToken, ValueTask<T?>> factory,
        CacheEntryOptions options,
        CancellationToken cancellationToken = default
    )
    {
        return cache.GetOrAddAsync(key, factory, options, cancellationToken);
    }

    public ValueTask<CacheValue<T>> GetOrAddAsync(
        string key,
        Func<CacheFactoryContext<T>, CancellationToken, ValueTask<CacheFactoryResult<T>>> factory,
        CacheEntryOptions options,
        CancellationToken cancellationToken = default
    )
    {
        return cache.GetOrAddAsync(key, factory, options, cancellationToken);
    }

    public ValueTask<bool> UpsertAsync(
        string cacheKey,
        T? cacheValue,
        TimeSpan? expiration,
        CancellationToken cancellationToken = default
    )
    {
        return cache.UpsertAsync(cacheKey, cacheValue, expiration, cancellationToken);
    }

    public ValueTask<bool> UpsertEntryAsync(
        string cacheKey,
        T? cacheValue,
        CacheEntryOptions options,
        CancellationToken cancellationToken = default
    )
    {
        return cache.UpsertEntryAsync(cacheKey, cacheValue, options, cancellationToken);
    }

    public ValueTask<int> UpsertAllAsync(
        IDictionary<string, T> value,
        TimeSpan? expiration,
        CancellationToken cancellationToken = default
    )
    {
        return cache.UpsertAllAsync(value, expiration, cancellationToken);
    }

    public ValueTask<bool> TryInsertAsync(
        string cacheKey,
        T? cacheValue,
        TimeSpan? expiration,
        CancellationToken cancellationToken = default
    )
    {
        return cache.TryInsertAsync(cacheKey, cacheValue, expiration, cancellationToken);
    }

    public ValueTask<bool> TryReplaceAsync(
        string key,
        T? value,
        TimeSpan? expiration,
        CancellationToken cancellationToken = default
    )
    {
        return cache.TryReplaceAsync(key, value, expiration, cancellationToken);
    }

    public ValueTask<bool> TryReplaceIfEqualAsync(
        string key,
        T? expected,
        T? value,
        TimeSpan? expiration,
        CancellationToken cancellationToken = default
    )
    {
        return cache.TryReplaceIfEqualAsync(key, expected, value, expiration, cancellationToken);
    }

    public ValueTask<long> SetAddAsync(
        string key,
        IEnumerable<T> value,
        TimeSpan? expiration,
        CancellationToken cancellationToken = default
    )
    {
        return cache.SetAddAsync(key, value, expiration, cancellationToken);
    }

    public ValueTask<IDictionary<string, CacheValue<T>>> GetAllAsync(
        IEnumerable<string> cacheKeys,
        CancellationToken cancellationToken = default
    )
    {
        return cache.GetAllAsync<T>(cacheKeys, cancellationToken);
    }

    public ValueTask<IDictionary<string, CacheValue<T>>> GetByPrefixAsync(
        string prefix,
        CancellationToken cancellationToken = default
    )
    {
        return cache.GetByPrefixAsync<T>(prefix, cancellationToken);
    }

    public ValueTask<CacheValue<T>> GetAsync(string cacheKey, CancellationToken cancellationToken = default)
    {
        return cache.GetAsync<T>(cacheKey, cancellationToken);
    }

    public ValueTask<CacheValue<ICollection<T>>> GetSetAsync(
        string key,
        int? pageIndex = null,
        int pageSize = 100,
        CancellationToken cancellationToken = default
    )
    {
        return cache.GetSetAsync<T>(key, pageIndex, pageSize, cancellationToken);
    }

    public ValueTask RefreshAsync(string cacheKey, CancellationToken cancellationToken = default)
    {
        return cache.RefreshAsync(cacheKey, cancellationToken);
    }

    public ValueTask<bool> RemoveAsync(string cacheKey, CancellationToken cancellationToken = default)
    {
        return cache.RemoveAsync(cacheKey, cancellationToken);
    }

    public ValueTask<bool> ExpireAsync(string cacheKey, CancellationToken cancellationToken = default)
    {
        return cache.ExpireAsync(cacheKey, cancellationToken);
    }

    public ValueTask<bool> RemoveIfEqualAsync(
        string cacheKey,
        T? expected,
        CancellationToken cancellationToken = default
    )
    {
        return cache.RemoveIfEqualAsync(cacheKey, expected, cancellationToken);
    }

    public ValueTask<int> RemoveByPrefixAsync(string prefix, CancellationToken cancellationToken = default)
    {
        return cache.RemoveByPrefixAsync(prefix, cancellationToken);
    }

    public ValueTask RemoveByTagAsync(string tag, CancellationToken cancellationToken = default)
    {
        return cache.RemoveByTagAsync(tag, cancellationToken);
    }

    public ValueTask ClearAsync(CancellationToken cancellationToken = default)
    {
        return cache.ClearAsync(cancellationToken);
    }

    public ValueTask<long> SetRemoveAsync(
        string key,
        IEnumerable<T> value,
        TimeSpan? expiration,
        CancellationToken cancellationToken = default
    )
    {
        return cache.SetRemoveAsync(key, value, expiration, cancellationToken);
    }

    public ValueTask<int> RemoveAllAsync(IEnumerable<string> cacheKeys, CancellationToken cancellationToken = default)
    {
        return cache.RemoveAllAsync(cacheKeys, cancellationToken);
    }

    public ValueTask FlushAsync(CancellationToken cancellationToken = default)
    {
        return cache.FlushAsync(cancellationToken);
    }

    public ValueTask<double> IncrementAsync(
        string key,
        double amount,
        TimeSpan? expiration,
        CancellationToken cancellationToken = default
    )
    {
        return cache.IncrementAsync(key, amount, expiration, cancellationToken);
    }

    public ValueTask<long> IncrementAsync(
        string key,
        long amount,
        TimeSpan? expiration,
        CancellationToken cancellationToken = default
    )
    {
        return cache.IncrementAsync(key, amount, expiration, cancellationToken);
    }

    public ValueTask<double> SetIfHigherAsync(
        string key,
        double value,
        TimeSpan? expiration,
        CancellationToken cancellationToken = default
    )
    {
        return cache.SetIfHigherAsync(key, value, expiration, cancellationToken);
    }

    public ValueTask<long> SetIfHigherAsync(
        string key,
        long value,
        TimeSpan? expiration,
        CancellationToken cancellationToken = default
    )
    {
        return cache.SetIfHigherAsync(key, value, expiration, cancellationToken);
    }

    public ValueTask<double> SetIfLowerAsync(
        string key,
        double value,
        TimeSpan? expiration,
        CancellationToken cancellationToken = default
    )
    {
        return cache.SetIfLowerAsync(key, value, expiration, cancellationToken);
    }

    public ValueTask<long> SetIfLowerAsync(
        string key,
        long value,
        TimeSpan? expiration,
        CancellationToken cancellationToken = default
    )
    {
        return cache.SetIfLowerAsync(key, value, expiration, cancellationToken);
    }

    public ValueTask<IReadOnlyList<string>> GetAllKeysByPrefixAsync(
        string prefix,
        CancellationToken cancellationToken = default
    )
    {
        return cache.GetAllKeysByPrefixAsync(prefix, cancellationToken);
    }

    public ValueTask<long> GetCountAsync(string prefix = "", CancellationToken cancellationToken = default)
    {
        return cache.GetCountAsync(prefix, cancellationToken);
    }

    public ValueTask<bool> ExistsAsync(string key, CancellationToken cancellationToken = default)
    {
        return cache.ExistsAsync(key, cancellationToken);
    }

    public ValueTask<TimeSpan?> GetExpirationAsync(string key, CancellationToken cancellationToken = default)
    {
        return cache.GetExpirationAsync(key, cancellationToken);
    }
}
