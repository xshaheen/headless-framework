// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Caching;

/// <summary>
/// Provider-agnostic contract for a key/value cache with factory-backed reads, fail-safe stale serving,
/// tag-based and generation-based logical invalidation, sliding expiration, and numeric/set primitives.
/// </summary>
/// <remarks>
/// Implementations include <see cref="IInMemoryCache"/> (process-local L1), <see cref="IRemoteCache"/>
/// (distributed L2), and the two-tier hybrid. All timestamps are UTC. Methods that accept
/// <see cref="CacheEntryOptions"/> validate them before any I/O; an invalid option set throws
/// <see cref="ArgumentOutOfRangeException"/> or <see cref="ArgumentException"/> before anything is written.
/// </remarks>
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
    /// <remarks>
    /// A <see cref="TimeSpan.Zero"/> expiration is treated as expire-immediately: any existing entry is evicted and
    /// the method returns <see langword="false"/> without writing a new value.
    /// </remarks>
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
    /// <see cref="CacheEntryOptions.Tags"/> are persisted on the entry for later <see cref="RemoveByTagAsync"/>
    /// logical invalidation. Options are validated with the same rules as <c>GetOrAddAsync</c>. Prefer
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

    /// <summary>
    /// Writes all entries in <paramref name="value"/> with the given <paramref name="expiration"/>.
    /// Returns the number of keys successfully written.
    /// </summary>
    /// <returns>The number of entries written.</returns>
    ValueTask<int> UpsertAllAsync<T>(
        IDictionary<string, T> value,
        TimeSpan? expiration,
        CancellationToken cancellationToken = default
    );

    /// <summary>
    /// Inserts <paramref name="value"/> only when <paramref name="key"/> does not already exist (add-only).
    /// </summary>
    /// <returns><see langword="true"/> when the key was absent and the value was inserted; <see langword="false"/> when the key already existed.</returns>
    ValueTask<bool> TryInsertAsync<T>(
        string key,
        T? value,
        TimeSpan? expiration,
        CancellationToken cancellationToken = default
    );

    /// <summary>
    /// Replaces the value only when <paramref name="key"/> already exists (update-only).
    /// </summary>
    /// <returns><see langword="true"/> when the key existed and was updated; <see langword="false"/> when the key was absent.</returns>
    ValueTask<bool> TryReplaceAsync<T>(
        string key,
        T? value,
        TimeSpan? expiration,
        CancellationToken cancellationToken = default
    );

    /// <summary>
    /// Atomically replaces the value only when the current stored value equals <paramref name="expected"/>
    /// (compare-and-swap). Useful for optimistic-concurrency updates without external locking.
    /// </summary>
    /// <returns><see langword="true"/> when the stored value matched <paramref name="expected"/> and was replaced; <see langword="false"/> otherwise.</returns>
    ValueTask<bool> TryReplaceIfEqualAsync<T>(
        string key,
        T? expected,
        T? value,
        TimeSpan? expiration,
        CancellationToken cancellationToken = default
    );

    /// <summary>
    /// Atomically adds <paramref name="amount"/> to the numeric value stored at <paramref name="key"/>,
    /// creating the key if absent, and resets the expiration to <paramref name="expiration"/>.
    /// </summary>
    /// <returns>The new value after the increment.</returns>
    /// <remarks>
    /// A <see cref="TimeSpan.Zero"/> expiration is treated as expire-immediately: the key is evicted and the method
    /// returns <c>0</c> without incrementing.
    /// </remarks>
    ValueTask<double> IncrementAsync(
        string key,
        double amount,
        TimeSpan? expiration,
        CancellationToken cancellationToken = default
    );

    /// <summary>
    /// Atomically adds <paramref name="amount"/> to the numeric value stored at <paramref name="key"/>,
    /// creating the key if absent, and resets the expiration to <paramref name="expiration"/>.
    /// </summary>
    /// <returns>The new value after the increment.</returns>
    /// <remarks>
    /// A <see cref="TimeSpan.Zero"/> expiration is treated as expire-immediately: the key is evicted and the method
    /// returns <c>0</c> without incrementing.
    /// </remarks>
    ValueTask<long> IncrementAsync(
        string key,
        long amount,
        TimeSpan? expiration,
        CancellationToken cancellationToken = default
    );

    /// <summary>
    /// Stores <paramref name="value"/> at <paramref name="key"/> only when it is greater than the current
    /// stored value. Returns the difference <c>(new - old)</c> when the store was updated, or <c>0</c> when
    /// the current value was already ≥ <paramref name="value"/>. When the key is absent the value is stored and
    /// that stored value is returned (there is no prior value to compute a difference against).
    /// </summary>
    /// <remarks>
    /// A <see cref="TimeSpan.Zero"/> expiration is treated as expire-immediately: the key is evicted and the method
    /// returns <c>0</c> without storing.
    /// </remarks>
    ValueTask<double> SetIfHigherAsync(
        string key,
        double value,
        TimeSpan? expiration,
        CancellationToken cancellationToken = default
    );

    /// <summary>
    /// Stores <paramref name="value"/> at <paramref name="key"/> only when it is greater than the current
    /// stored value. Returns the difference <c>(new - old)</c> when the store was updated, or <c>0</c> when
    /// the current value was already ≥ <paramref name="value"/>. When the key is absent the value is stored and
    /// that stored value is returned (there is no prior value to compute a difference against).
    /// </summary>
    /// <remarks>
    /// A <see cref="TimeSpan.Zero"/> expiration is treated as expire-immediately: the key is evicted and the method
    /// returns <c>0</c> without storing.
    /// </remarks>
    ValueTask<long> SetIfHigherAsync(
        string key,
        long value,
        TimeSpan? expiration,
        CancellationToken cancellationToken = default
    );

    /// <summary>
    /// Stores <paramref name="value"/> at <paramref name="key"/> only when it is less than the current
    /// stored value. Returns the difference <c>(old - new)</c> when the store was updated, or <c>0</c> when
    /// the current value was already ≤ <paramref name="value"/>. When the key is absent the value is stored and
    /// that stored value is returned (there is no prior value to compute a difference against).
    /// </summary>
    /// <remarks>
    /// A <see cref="TimeSpan.Zero"/> expiration is treated as expire-immediately: the key is evicted and the method
    /// returns <c>0</c> without storing.
    /// </remarks>
    ValueTask<double> SetIfLowerAsync(
        string key,
        double value,
        TimeSpan? expiration,
        CancellationToken cancellationToken = default
    );

    /// <summary>
    /// Stores <paramref name="value"/> at <paramref name="key"/> only when it is less than the current
    /// stored value. Returns the difference <c>(old - new)</c> when the store was updated, or <c>0</c> when
    /// the current value was already ≤ <paramref name="value"/>. When the key is absent the value is stored and
    /// that stored value is returned (there is no prior value to compute a difference against).
    /// </summary>
    /// <remarks>
    /// A <see cref="TimeSpan.Zero"/> expiration is treated as expire-immediately: the key is evicted and the method
    /// returns <c>0</c> without storing.
    /// </remarks>
    ValueTask<long> SetIfLowerAsync(
        string key,
        long value,
        TimeSpan? expiration,
        CancellationToken cancellationToken = default
    );

    /// <summary>
    /// Adds members to the set stored at <paramref name="key"/>, creating the set if absent. String members
    /// are compared case-insensitively; other types use default equality. Null members are silently skipped.
    /// Returns the number of members actually added (duplicates excluded).
    /// </summary>
    ValueTask<long> SetAddAsync<T>(
        string key,
        IEnumerable<T> value,
        TimeSpan? expiration,
        CancellationToken cancellationToken = default
    );

    #endregion

    #region Get

    /// <summary>Reads many keys in one call and returns a result for each, including misses.</summary>
    /// <returns>A dictionary keyed by the original cache keys; each value is a <see cref="CacheValue{T}"/> indicating hit or miss.</returns>
    ValueTask<IDictionary<string, CacheValue<T>>> GetAllAsync<T>(
        IEnumerable<string> cacheKeys,
        CancellationToken cancellationToken = default
    );

    /// <summary>
    /// Reads all entries whose key starts with <paramref name="prefix"/>. The semantics are the same as
    /// <c>GetAsync</c> applied to each matching key: logically expired and tag-invalidated entries are
    /// excluded; physically present fail-safe reserves are NOT returned (direct reads return misses on stale).
    /// </summary>
    /// <returns>A dictionary keyed by cache key containing only hits.</returns>
    ValueTask<IDictionary<string, CacheValue<T>>> GetByPrefixAsync<T>(
        string prefix,
        CancellationToken cancellationToken = default
    );

    /// <summary>Returns all keys whose name starts with <paramref name="prefix"/>. Includes physically-expired and logically-expired keys: callers should re-check existence before use.</summary>
    ValueTask<IReadOnlyList<string>> GetAllKeysByPrefixAsync(
        string prefix,
        CancellationToken cancellationToken = default
    );

    /// <summary>
    /// Gets the value for <paramref name="key"/>. Returns <see cref="CacheValue{T}.NoValue"/> on a miss,
    /// logical expiry, or tag-invalidation; the fail-safe physical reserve, if any, is left intact for
    /// <c>GetOrAddAsync</c> to serve on a subsequent factory failure.
    /// </summary>
    ValueTask<CacheValue<T>> GetAsync<T>(string key, CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns the count of non-expired entries, optionally filtered to those whose key starts with
    /// <paramref name="prefix"/>. The count is approximate on distributed stores.
    /// </summary>
    ValueTask<long> GetCountAsync(string prefix = "", CancellationToken cancellationToken = default);

    /// <summary>Returns <see langword="true"/> when <paramref name="key"/> exists and is not logically expired or tag-invalidated.</summary>
    ValueTask<bool> ExistsAsync(string key, CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns the remaining logical TTL for <paramref name="key"/>, or <see langword="null"/> when the key
    /// is absent, logically expired, tag-invalidated, or was written without an explicit expiration.
    /// </summary>
    ValueTask<TimeSpan?> GetExpirationAsync(string key, CancellationToken cancellationToken = default);

    /// <summary>
    /// Reads an optionally-paginated page of members from the set stored at <paramref name="key"/>. Members
    /// that have individually expired within the set are excluded. Returns <see cref="CacheValue{T}.NoValue"/>
    /// when the key is absent. <paramref name="pageIndex"/> is 1-based; pass <see langword="null"/> to
    /// return all members.
    /// </summary>
    ValueTask<CacheValue<ICollection<T>>> GetSetAsync<T>(
        string key,
        int? pageIndex = null,
        int pageSize = 100,
        CancellationToken cancellationToken = default
    );

    /// <summary>
    /// Re-arms a sliding cache entry's idle window without materializing its value.
    /// </summary>
    /// <remarks>
    /// This is a no-op when the key is absent, when the entry is not sliding, or when the entry is already past
    /// its absolute physical cap. Implementations apply the same throttling used by value-returning reads.
    /// </remarks>
    /// <param name="key">The cache key.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    ValueTask RefreshAsync(string key, CancellationToken cancellationToken = default);

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

    /// <summary>
    /// Removes <paramref name="key"/> only when the current stored value equals <paramref name="expected"/>
    /// (compare-and-delete). Returns <see langword="false"/> when the key is absent or the value did not match.
    /// </summary>
    ValueTask<bool> RemoveIfEqualAsync<T>(string key, T? expected, CancellationToken cancellationToken = default);

    /// <summary>Removes all specified keys. Returns the number of keys that were present and removed.</summary>
    ValueTask<int> RemoveAllAsync(IEnumerable<string> cacheKeys, CancellationToken cancellationToken = default);

    /// <summary>Removes all keys whose name starts with <paramref name="prefix"/>. Returns the number of keys removed.</summary>
    ValueTask<int> RemoveByPrefixAsync(string prefix, CancellationToken cancellationToken = default);

    /// <summary>
    /// Logically invalidates every entry that carries <paramref name="tag"/> in O(1) by writing a per-tag
    /// invalidation marker (a timestamp), without enumerating members. On the next read, an entry whose birth
    /// time (<c>CreatedAt</c>) predates the marker is treated as a miss by direct reads and demoted to a
    /// fail-safe reserve by the factory coordinator (so a failing factory can still serve it stale). A key
    /// re-created after the marker (a newer birth time) is NOT invalidated, so tag memberships remain pinned to
    /// the entry version. The marker is per-tier; the physical TTL backstops staleness if a marker is lost. On a
    /// two-tier cache the marker is bumped on both tiers and propagated to other instances.
    /// </summary>
    /// <param name="tag">The invalidation tag.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    ValueTask RemoveByTagAsync(string tag, CancellationToken cancellationToken = default);

    /// <summary>
    /// LOGICALLY clears the cache in O(1) by bumping a single reserved clear-generation marker: every entry born
    /// before the bump is treated as a miss by direct reads and demoted to a fail-safe reserve by the factory
    /// coordinator, so a failing factory can still serve stale values (the fail-safe reserves are preserved).
    /// This is the reserve-preserving counterpart of <see cref="FlushAsync"/>, which drops the fail-safe reserves.
    /// Prefer <see cref="ClearAsync"/> when you want fail-safe coverage to outlive the clear; use
    /// <see cref="FlushAsync"/> to drop everything including reserves. The marker is per-tier and, on a two-tier
    /// cache, is bumped on both tiers and propagated to other instances. A re-created entry (newer birth time) is
    /// unaffected.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    ValueTask ClearAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Removes the specified members from the set stored at <paramref name="key"/>.
    /// Returns the number of members that were present and removed.
    /// </summary>
    ValueTask<long> SetRemoveAsync<T>(
        string key,
        IEnumerable<T> value,
        TimeSpan? expiration,
        CancellationToken cancellationToken = default
    );

    /// <summary>
    /// Flushes the whole cache, dropping every entry <em>including its fail-safe reserve</em> — the reserve-dropping
    /// counterpart of <see cref="ClearAsync"/>. After a flush a failing factory cannot serve a stale value. The
    /// removal mechanism is tier-specific: an in-process cache wipes physically (freeing memory immediately); a
    /// distributed cache bumps a reserved remove-generation marker (cluster-safe, no physical <c>FLUSHDB</c>), so its
    /// entries read as a hard miss while physical memory is reclaimed lazily by each entry's TTL.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    ValueTask FlushAsync(CancellationToken cancellationToken = default);

    #endregion
}

/// <summary>
/// The in-memory (local / L1) tier contract. Carries no members beyond <see cref="ICache"/>: it is a deliberate
/// tier marker, not a behavioral extension. Multi-tier composition (for example the hybrid cache resolving its
/// L1 tier) selects the in-memory tier by this type, distinctly from <see cref="IRemoteCache"/> — both are
/// <see cref="ICache"/>, so the marker is what disambiguates them in DI. Do not remove it for being empty: a
/// hybrid host depends on this type to resolve the local tier.
/// </summary>
[PublicAPI]
public interface IInMemoryCache : ICache;

/// <summary>
/// The distributed (remote / L2) tier contract. Extends <see cref="ICache"/> with single round-trip
/// value-plus-expiration reads (used to mirror entries into a faster local tier) and also serves as the tier
/// marker that multi-tier composition (the hybrid cache resolving its L2 tier) selects distinctly from
/// <see cref="IInMemoryCache"/>.
/// </summary>
[PublicAPI]
public interface IRemoteCache : ICache
{
    /// <summary>
    /// Reads a single key in one round-trip and returns the hit's value together with its remaining
    /// logical expiration, so callers can mirror the value into a local tier without a separate
    /// expiration query. When the key is not found the returned <see cref="CacheValueWithExpiration{T}.Value"/>
    /// will have <see cref="CacheValue{T}.HasValue"/> equal to <see langword="false"/>.
    /// </summary>
    /// <remarks>
    /// The expiration in the returned <see cref="CacheValueWithExpiration{T}"/> is the remaining logical TTL at
    /// the moment the entry was read. For entries written without explicit logical-expiry metadata (legacy
    /// payloads) the <see cref="CacheValueWithExpiration{T}.Expiration"/> is <see langword="null"/>.
    /// Entries whose logical TTL has already elapsed are treated as misses.
    /// </remarks>
    /// <typeparam name="T">The type of the cached value.</typeparam>
    /// <param name="key">The cache key to look up.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>
    /// A <see cref="CacheValueWithExpiration{T}"/> containing the value and remaining logical expiration
    /// when the key is found and has not yet logically expired; otherwise a result whose
    /// <see cref="CacheValueWithExpiration{T}.Value"/> has <see cref="CacheValue{T}.HasValue"/> equal to
    /// <see langword="false"/>.
    /// </returns>
    ValueTask<CacheValueWithExpiration<T>> GetWithExpirationAsync<T>(
        string key,
        CancellationToken cancellationToken = default
    );

    /// <summary>
    /// Reads multiple keys in one round-trip and returns each hit's value together with its remaining
    /// logical expiration, so callers can mirror the value into a local tier without a separate per-key
    /// expiration query. Keys not found in the remote store are omitted from the result.
    /// </summary>
    /// <remarks>
    /// The expiration in each <see cref="CacheValueWithExpiration{T}"/> is the remaining logical TTL at
    /// the moment the entry was read. For entries written without explicit logical-expiry metadata (legacy
    /// payloads) the <see cref="CacheValueWithExpiration{T}.Expiration"/> is <see langword="null"/>.
    /// Entries whose logical TTL has already elapsed are excluded from the result (treated as misses).
    /// </remarks>
    /// <typeparam name="T">The type of the cached values.</typeparam>
    /// <param name="cacheKeys">The keys to look up.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>
    /// A dictionary keyed by cache key containing the value and remaining logical expiration for every
    /// key that was found and has not yet logically expired. Keys not present in the store are absent
    /// from the dictionary.
    /// </returns>
    ValueTask<IDictionary<string, CacheValueWithExpiration<T>>> GetAllWithExpirationAsync<T>(
        IEnumerable<string> cacheKeys,
        CancellationToken cancellationToken = default
    );
}
