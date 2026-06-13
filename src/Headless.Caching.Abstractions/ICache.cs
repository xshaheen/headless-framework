// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Caching;

[PublicAPI]
public interface ICache
{
    /// <summary>
    /// Gets the default <see cref="CacheEntryOptions"/> configured for this cache instance at registration
    /// (for example via the provider options' <c>DefaultEntryOptions</c>). Used by the option-less
    /// <c>GetOrAddAsync</c> extension overloads; when <see langword="null"/>, those overloads throw
    /// <see cref="InvalidOperationException"/> — defaults are explicit-at-registration, never magic.
    /// </summary>
    CacheEntryOptions? DefaultEntryOptions { get; }

    /// <summary>
    /// Gets a value from cache, or creates it using the factory if not found.
    /// Uses keyed locking to prevent cache stampedes (multiple concurrent factory executions for the same key).
    /// </summary>
    /// <typeparam name="T">The type of the cached value.</typeparam>
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
    ValueTask<CacheValue<T>> GetOrAddAsync<T>(
        string key,
        Func<CancellationToken, ValueTask<T?>> factory,
        CacheEntryOptions options,
        CancellationToken cancellationToken = default
    );

    /// <summary>
    /// Gets a value from cache, or refreshes it using a conditional factory (the HTTP-304 pattern).
    /// The factory receives a <see cref="CacheFactoryContext{T}"/> carrying the last-known cached value and its
    /// validators (<see cref="CacheFactoryContext{T}.ETag"/>, <see cref="CacheFactoryContext{T}.LastModifiedAt"/>)
    /// and returns <see cref="CacheFactoryContext{T}.NotModified"/> to extend the existing entry as fresh, or
    /// <see cref="CacheFactoryContext{T}.Modified(T, string?, DateTime?)"/> to replace it. The factory may also
    /// replace <see cref="CacheFactoryContext{T}.Options"/> before returning (adaptive caching).
    /// Uses keyed locking to prevent cache stampedes, like the simple overload.
    /// </summary>
    /// <typeparam name="T">The type of the cached value.</typeparam>
    /// <param name="key">The cache key.</param>
    /// <param name="factory">The conditional factory invoked on a miss or refresh. Receives the per-execution context and the cancellation token.</param>
    /// <param name="options">Cache entry options for the cached value; the factory may replace them via the context.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The cached, extended, or newly created value wrapped in <see cref="CacheValue{T}"/>.</returns>
    /// <exception cref="ArgumentOutOfRangeException">
    /// Thrown when the entry options (including an adaptive replacement set by the factory) are invalid, for
    /// example a non-positive <see cref="CacheEntryOptions.Duration"/>.
    /// </exception>
    ValueTask<CacheValue<T>> GetOrAddAsync<T>(
        string key,
        Func<CacheFactoryContext<T>, CancellationToken, ValueTask<CacheFactoryResult<T>>> factory,
        CacheEntryOptions options,
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

    /// <summary>
    /// Sets a value as a direct write honoring the full <see cref="CacheEntryOptions"/> semantics: the entry is
    /// stamped exactly like a fresh factory write (fail-safe extends physical retention, an eager-refresh
    /// threshold stamps the eager point, sliding expiration clamps the logical lifetime) and
    /// <see cref="CacheEntryOptions.Tags"/> are persisted for later <see cref="RemoveByTagAsync"/> invalidation.
    /// Options are validated with the same rules as <c>GetOrAddAsync</c>. This method performs a
    /// read-before-write to reconcile provider tag indexes, so prefer
    /// <see cref="UpsertAsync{T}(string, T, TimeSpan?, CancellationToken)"/> on hot paths that need none of the
    /// per-entry option semantics. (Named distinctly because the <see cref="TimeSpan"/>-to-options implicit
    /// conversion would otherwise make every bare-<see cref="TimeSpan"/> upsert ambiguous.)
    /// </summary>
    /// <typeparam name="T">The type of the cached value.</typeparam>
    /// <param name="key">The cache key.</param>
    /// <param name="value">The value to cache; <see langword="null"/> caches the null sentinel.</param>
    /// <param name="options">The cache entry options applied to the written entry.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns><see langword="true"/> when the write was issued.</returns>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when the entry options are invalid, for example a non-positive <see cref="CacheEntryOptions.Duration"/>.</exception>
    /// <exception cref="ArgumentException">Thrown when <see cref="CacheEntryOptions.Tags"/> contains an empty tag or exceeds the supported tag count/length limits.</exception>
    ValueTask<bool> UpsertEntryAsync<T>(
        string key,
        T? value,
        CacheEntryOptions options,
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
    ValueTask<long> GetCountAsync(string prefix = "", CancellationToken cancellationToken = default);

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

    /// <summary>
    /// Logically expires the entry instead of removing it: normal reads immediately treat it as a miss, but its
    /// fail-safe physical reserve is preserved, so a subsequent <c>GetOrAddAsync</c> whose factory fails can still
    /// serve the stale value (the fail-safe parachute). For an entry written WITHOUT fail-safe (no physical
    /// reserve beyond its logical lifetime) this is equivalent to <see cref="RemoveAsync"/>. On a two-tier cache
    /// the expiration is applied to both tiers and propagated to other instances so their copies are logically
    /// expired too (reserves preserved). A no-op when the key is absent.
    /// </summary>
    /// <param name="key">The cache key to logically expire.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns><see langword="true"/> when an entry was found and expired; otherwise <see langword="false"/>.</returns>
    ValueTask<bool> ExpireAsync(string key, CancellationToken cancellationToken = default);

    /// <summary>Remove only if equal the expected value.</summary>
    ValueTask<bool> RemoveIfEqualAsync<T>(string key, T? expected, CancellationToken cancellationToken = default);

    /// <summary>Removes all.</summary>
    ValueTask<int> RemoveAllAsync(IEnumerable<string> cacheKeys, CancellationToken cancellationToken = default);

    /// <summary>Removes cached item by cache key's prefix.</summary>
    ValueTask<int> RemoveByPrefixAsync(string prefix, CancellationToken cancellationToken = default);

    /// <summary>
    /// Removes exactly the entries that CURRENTLY carry <paramref name="tag"/> (assigned via
    /// <see cref="CacheEntryOptions.Tags"/> or <see cref="CacheFactoryContext{T}.Tags"/>). A key that expired or
    /// was re-created without the tag is NOT removed: tag memberships are pinned to the entry version, so a
    /// later untagged write over the same key invalidates the stale membership instead of removing the new entry.
    /// </summary>
    /// <param name="tag">The invalidation tag.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The number of entries removed.</returns>
    ValueTask<int> RemoveByTagAsync(string tag, CancellationToken cancellationToken = default);

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

public interface IInMemoryCache : ICache
{
    /// <summary>
    /// Returns the keys currently indexed under <paramref name="tag"/>. The snapshot may be momentarily stale
    /// under concurrent writes (an untagged overwrite can race the index update), so callers must treat the
    /// result as advisory and verify live-entry membership before acting on individual keys.
    /// </summary>
    IReadOnlyCollection<string> GetTaggedKeys(string tag);
}

public interface IRemoteCache : ICache;
