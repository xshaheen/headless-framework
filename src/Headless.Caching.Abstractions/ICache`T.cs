// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Caching;

[PublicAPI]
public interface ICache<T>
{
    /// <summary>
    /// Gets the default <see cref="CacheEntryOptions"/> configured for this cache instance at registration.
    /// Used by the option-less <c>GetOrAddAsync</c> extension overloads; when <see langword="null"/>,
    /// those overloads throw <see cref="InvalidOperationException"/> — defaults are explicit-at-registration,
    /// never magic.
    /// </summary>
    CacheEntryOptions? DefaultEntryOptions { get; }

    /// <summary>
    /// Gets a value from cache, or creates it using the factory if not found.
    /// Uses keyed locking to prevent cache stampedes (multiple concurrent factory executions for the same key).
    /// </summary>
    /// <param name="key">The cache key.</param>
    /// <param name="factory">The factory function to create the value if not found in cache. Receives the cancellation token.</param>
    /// <param name="options">Cache entry options for the cached value.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The cached or newly created value wrapped in <see cref="CacheValue{T}"/>.</returns>
    /// <exception cref="ArgumentOutOfRangeException">
    /// Thrown when <see cref="CacheEntryOptions.Duration"/> is not positive, or when fail-safe is enabled and
    /// <see cref="CacheEntryOptions.FailSafeMaxDuration"/> or
    /// <see cref="CacheEntryOptions.FailSafeThrottleDuration"/> is not positive.
    /// </exception>
    ValueTask<CacheValue<T>> GetOrAddAsync(
        string key,
        Func<CancellationToken, ValueTask<T?>> factory,
        CacheEntryOptions options,
        CancellationToken cancellationToken = default
    );

    /// <summary>
    /// Gets a value from cache, or refreshes it using a conditional factory (the HTTP-304 pattern).
    /// The factory receives a <see cref="CacheFactoryContext{T}"/> carrying the last-known cached value and its
    /// validators and returns <see cref="CacheFactoryContext{T}.NotModified"/> to extend the existing entry as
    /// fresh, or <see cref="CacheFactoryContext{T}.Modified(T, string?, DateTime?)"/> to replace it. The factory
    /// may also replace <see cref="CacheFactoryContext{T}.Options"/> before returning (adaptive caching).
    /// </summary>
    /// <param name="key">The cache key.</param>
    /// <param name="factory">The conditional factory invoked on a miss or refresh. Receives the per-execution context and the cancellation token.</param>
    /// <param name="options">Cache entry options for the cached value; the factory may replace them via the context.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The cached, extended, or newly created value wrapped in <see cref="CacheValue{T}"/>.</returns>
    /// <exception cref="ArgumentOutOfRangeException">
    /// Thrown when the entry options (including an adaptive replacement set by the factory) are invalid, for
    /// example a non-positive <see cref="CacheEntryOptions.Duration"/>.
    /// </exception>
    ValueTask<CacheValue<T>> GetOrAddAsync(
        string key,
        Func<CacheFactoryContext<T>, CancellationToken, ValueTask<CacheFactoryResult<T>>> factory,
        CacheEntryOptions options,
        CancellationToken cancellationToken = default
    );

    #region Update

    ValueTask<bool> UpsertAsync(
        string cacheKey,
        T? cacheValue,
        TimeSpan? expiration,
        CancellationToken cancellationToken = default
    );

    /// <summary>
    /// Sets a value as a direct write honoring the full <see cref="CacheEntryOptions"/> semantics, including
    /// <see cref="CacheEntryOptions.Tags"/> for later <see cref="RemoveByTagAsync"/> invalidation.
    /// </summary>
    ValueTask<bool> UpsertEntryAsync(
        string cacheKey,
        T? cacheValue,
        CacheEntryOptions options,
        CancellationToken cancellationToken = default
    );

    ValueTask<int> UpsertAllAsync(
        IDictionary<string, T> value,
        TimeSpan? expiration,
        CancellationToken cancellationToken = default
    );

    ValueTask<bool> TryInsertAsync(
        string cacheKey,
        T? cacheValue,
        TimeSpan? expiration,
        CancellationToken cancellationToken = default
    );

    ValueTask<bool> TryReplaceAsync(
        string key,
        T? value,
        TimeSpan? expiration,
        CancellationToken cancellationToken = default
    );

    ValueTask<bool> TryReplaceIfEqualAsync(
        string key,
        T? expected,
        T? value,
        TimeSpan? expiration,
        CancellationToken cancellationToken = default
    );

    ValueTask<long> SetAddAsync(
        string key,
        IEnumerable<T> value,
        TimeSpan? expiration,
        CancellationToken cancellationToken = default
    );

    #endregion

    #region Get

    ValueTask<IDictionary<string, CacheValue<T>>> GetAllAsync(
        IEnumerable<string> cacheKeys,
        CancellationToken cancellationToken = default
    );

    ValueTask<IDictionary<string, CacheValue<T>>> GetByPrefixAsync(
        string prefix,
        CancellationToken cancellationToken = default
    );

    ValueTask<CacheValue<T>> GetAsync(string cacheKey, CancellationToken cancellationToken = default);

    ValueTask<CacheValue<ICollection<T>>> GetSetAsync(
        string key,
        int? pageIndex = null,
        int pageSize = 100,
        CancellationToken cancellationToken = default
    );

    #endregion

    #region Remove

    ValueTask<bool> RemoveAsync(string cacheKey, CancellationToken cancellationToken = default);

    /// <summary>Logically expires the entry, preserving its fail-safe reserve. See <see cref="ICache.ExpireAsync"/>.</summary>
    ValueTask<bool> ExpireAsync(string cacheKey, CancellationToken cancellationToken = default);

    ValueTask<bool> RemoveIfEqualAsync(string cacheKey, T? expected, CancellationToken cancellationToken = default);

    ValueTask<int> RemoveByPrefixAsync(string prefix, CancellationToken cancellationToken = default);

    /// <summary>Logically invalidates entries carrying <paramref name="tag"/> in O(1). See <see cref="ICache.RemoveByTagAsync"/>.</summary>
    ValueTask RemoveByTagAsync(string tag, CancellationToken cancellationToken = default);

    /// <summary>Logically clears the cache in O(1), preserving fail-safe reserves. See <see cref="ICache.ClearAsync"/>.</summary>
    ValueTask ClearAsync(CancellationToken cancellationToken = default);

    ValueTask<long> SetRemoveAsync(
        string key,
        IEnumerable<T> value,
        TimeSpan? expiration,
        CancellationToken cancellationToken = default
    );

    /// <summary>Removes all cached items for the specified cache keys.</summary>
    ValueTask<int> RemoveAllAsync(IEnumerable<string> cacheKeys, CancellationToken cancellationToken = default);

    /// <summary>
    /// Flushes the whole cache, dropping every entry including its fail-safe reserve (reserve-dropping counterpart of
    /// <see cref="ClearAsync"/>). See <see cref="ICache.FlushAsync"/> for the tier-specific removal mechanism.
    /// </summary>
    ValueTask FlushAsync(CancellationToken cancellationToken = default);

    #endregion

    #region Management

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

    /// <summary>Gets all keys by prefix.</summary>
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

[PublicAPI]
public class Cache<T>(ICache cache) : ICache<T>
{
    /// <inheritdoc />
    public CacheEntryOptions? DefaultEntryOptions => cache.DefaultEntryOptions;

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
