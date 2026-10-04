// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Caching;

/// <summary>
/// Defines a provider-agnostic contract for a key and value cache with factory-backed reads, fail-safe stale serving,
/// tag-based and generation-based logical invalidation, sliding expiration, and numeric or set primitives.
/// </summary>
/// <remarks>
/// Implementations include <see cref="IInMemoryCache"/> for process-local L1 storage, <see cref="IRemoteCache"/>
/// for distributed L2 storage, and two-tier hybrid caching. All timestamps use UTC.
/// </remarks>
[PublicAPI]
public interface ICache
{
    /// <summary>
    /// Gets the default <see cref="CacheEntryOptions"/> configured for this cache instance at registration.
    /// Used by overloads that omit options. When <see langword="null"/>, those overloads throw <see cref="InvalidOperationException"/>.
    /// </summary>
    CacheEntryOptions? DefaultEntryOptions { get; }

    /// <summary>
    /// Gets the in-process event surface for this cache instance. The default implementation returns <see cref="CacheEvents.NoOp"/>.
    /// </summary>
    ICacheEvents Events => CacheEvents.NoOp;

    /// <summary>
    /// Gets a value from the cache, or creates and stores it using the factory when missing.
    /// Uses keyed locking to prevent concurrent factory executions for the same key.
    /// </summary>
    /// <typeparam name="T">The type of the cached value.</typeparam>
    /// <param name="key">The cache key.</param>
    /// <param name="factory">The factory function to create the value when not found in the cache.</param>
    /// <param name="options">Cache entry options for the cached value.</param>
    /// <param name="cancellationToken">A token to monitor for cancellation requests.</param>
    /// <returns>The cached or newly created value wrapped in <see cref="CacheValue{T}"/>.</returns>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <see cref="CacheEntryOptions.Duration"/> is not positive, or fail-safe is enabled and
    /// <see cref="CacheEntryOptions.FailSafeMaxDuration"/> or <see cref="CacheEntryOptions.FailSafeThrottleDuration"/> is not positive.
    /// </exception>
    ValueTask<CacheValue<T>> GetOrAddAsync<T>(
        string key,
        Func<CancellationToken, ValueTask<T?>> factory,
        CacheEntryOptions options,
        CancellationToken cancellationToken = default
    );

    /// <summary>
    /// Gets a value from the cache, or refreshes it using a conditional factory.
    /// The factory receives a <see cref="CacheFactoryContext{T}"/> and returns <see cref="CacheFactoryContext{T}.NotModified"/>
    /// to extend the existing entry, or <see cref="CacheFactoryContext{T}.Modified(T, string?, DateTime?)"/> to replace it.
    /// Uses keyed locking to prevent concurrent factory executions for the same key.
    /// </summary>
    /// <typeparam name="T">The type of the cached value.</typeparam>
    /// <param name="key">The cache key.</param>
    /// <param name="factory">The conditional factory invoked on a miss or refresh.</param>
    /// <param name="options">Cache entry options for the cached value.</param>
    /// <param name="cancellationToken">A token to monitor for cancellation requests.</param>
    /// <returns>The cached, extended, or newly created value wrapped in <see cref="CacheValue{T}"/>.</returns>
    /// <exception cref="ArgumentOutOfRangeException">
    /// The entry options or an adaptive replacement set by the factory are invalid.
    /// </exception>
    ValueTask<CacheValue<T>> GetOrAddAsync<T>(
        string key,
        Func<CacheFactoryContext<T>, CancellationToken, ValueTask<CacheFactoryResult<T>>> factory,
        CacheEntryOptions options,
        CancellationToken cancellationToken = default
    );

    #region Update

    /// <summary>Sets the specified value and expiration for a key.</summary>
    /// <remarks>
    /// An expiration of <see cref="TimeSpan.Zero"/> triggers immediate expiration: any existing entry is evicted and
    /// the method returns <see langword="false"/> without storing a new value.
    /// </remarks>
    ValueTask<bool> UpsertAsync<T>(
        string key,
        T? value,
        TimeSpan? expiration,
        CancellationToken cancellationToken = default
    );

    /// <summary>
    /// Sets a value directly while honoring <see cref="CacheEntryOptions"/> metadata.
    /// </summary>
    /// <typeparam name="T">The type of the cached value.</typeparam>
    /// <param name="key">The cache key.</param>
    /// <param name="value">The value to cache, or <see langword="null"/> to store an explicit null entry.</param>
    /// <param name="options">The cache entry options applied to the written entry.</param>
    /// <param name="cancellationToken">A token to monitor for cancellation requests.</param>
    /// <returns><see langword="true"/> when the write was issued; otherwise, <see langword="false"/>.</returns>
    /// <exception cref="ArgumentOutOfRangeException">The entry options are invalid.</exception>
    /// <exception cref="ArgumentException"><see cref="CacheEntryOptions.Tags"/> contains an empty tag or exceeds length limits.</exception>
    ValueTask<bool> UpsertEntryAsync<T>(
        string key,
        T? value,
        CacheEntryOptions options,
        CancellationToken cancellationToken = default
    );

    /// <summary>
    /// Writes all entries in <paramref name="value"/> with the specified <paramref name="expiration"/>.
    /// </summary>
    /// <returns>The number of entries successfully written.</returns>
    ValueTask<int> UpsertAllAsync<T>(
        IDictionary<string, T> value,
        TimeSpan? expiration,
        CancellationToken cancellationToken = default
    );

    /// <summary>
    /// Inserts <paramref name="value"/> only when <paramref name="key"/> does not exist.
    /// </summary>
    /// <returns><see langword="true"/> when the key was absent and the value was inserted; otherwise, <see langword="false"/>.</returns>
    ValueTask<bool> TryInsertAsync<T>(
        string key,
        T? value,
        TimeSpan? expiration,
        CancellationToken cancellationToken = default
    );

    /// <summary>
    /// Replaces the value only when <paramref name="key"/> already exists.
    /// </summary>
    /// <returns><see langword="true"/> when the key existed and was updated; otherwise, <see langword="false"/>.</returns>
    ValueTask<bool> TryReplaceAsync<T>(
        string key,
        T? value,
        TimeSpan? expiration,
        CancellationToken cancellationToken = default
    );

    /// <summary>
    /// Atomically replaces the value only when the current stored value equals <paramref name="expected"/>.
    /// </summary>
    /// <returns><see langword="true"/> when the stored value matched <paramref name="expected"/> and was replaced; otherwise, <see langword="false"/>.</returns>
    ValueTask<bool> TryReplaceIfEqualAsync<T>(
        string key,
        T? expected,
        T? value,
        TimeSpan? expiration,
        CancellationToken cancellationToken = default
    );

    /// <summary>
    /// Atomically adds <paramref name="amount"/> to the numeric value stored at <paramref name="key"/>,
    /// creating the key if absent, and resets the expiration.
    /// </summary>
    /// <returns>The new value after the increment.</returns>
    /// <remarks>
    /// An expiration of <see cref="TimeSpan.Zero"/> evicts the key and returns <c>0</c> without incrementing.
    /// </remarks>
    ValueTask<double> IncrementAsync(
        string key,
        double amount,
        TimeSpan? expiration,
        CancellationToken cancellationToken = default
    );

    /// <summary>
    /// Atomically adds <paramref name="amount"/> to the numeric value stored at <paramref name="key"/>,
    /// creating the key if absent, and resets the expiration.
    /// </summary>
    /// <returns>The new value after the increment.</returns>
    /// <remarks>
    /// An expiration of <see cref="TimeSpan.Zero"/> evicts the key and returns <c>0</c> without incrementing.
    /// </remarks>
    ValueTask<long> IncrementAsync(
        string key,
        long amount,
        TimeSpan? expiration,
        CancellationToken cancellationToken = default
    );

    /// <summary>
    /// Stores <paramref name="value"/> at <paramref name="key"/> only when it is greater than the current stored value.
    /// </summary>
    /// <returns>The positive difference when updated, the new value when the key was absent, or <c>0</c> when not updated.</returns>
    ValueTask<double> SetIfHigherAsync(
        string key,
        double value,
        TimeSpan? expiration,
        CancellationToken cancellationToken = default
    );

    /// <summary>
    /// Stores <paramref name="value"/> at <paramref name="key"/> only when it is greater than the current stored value.
    /// </summary>
    /// <returns>The positive difference when updated, the new value when the key was absent, or <c>0</c> when not updated.</returns>
    ValueTask<long> SetIfHigherAsync(
        string key,
        long value,
        TimeSpan? expiration,
        CancellationToken cancellationToken = default
    );

    /// <summary>
    /// Stores <paramref name="value"/> at <paramref name="key"/> only when it is less than the current stored value.
    /// </summary>
    /// <returns>The positive difference when updated, the new value when the key was absent, or <c>0</c> when not updated.</returns>
    ValueTask<double> SetIfLowerAsync(
        string key,
        double value,
        TimeSpan? expiration,
        CancellationToken cancellationToken = default
    );

    /// <summary>
    /// Stores <paramref name="value"/> at <paramref name="key"/> only when it is less than the current stored value.
    /// </summary>
    /// <returns>The positive difference when updated, the new value when the key was absent, or <c>0</c> when not updated.</returns>
    ValueTask<long> SetIfLowerAsync(
        string key,
        long value,
        TimeSpan? expiration,
        CancellationToken cancellationToken = default
    );

    /// <summary>
    /// Adds members to the set stored at <paramref name="key"/>, creating the set when absent.
    /// </summary>
    /// <returns>The number of members added, excluding duplicates.</returns>
    ValueTask<long> SetAddAsync<T>(
        string key,
        IEnumerable<T> value,
        TimeSpan? expiration,
        CancellationToken cancellationToken = default
    );

    #endregion

    #region Get

    /// <summary>Reads multiple keys in one call and returns a result envelope for each key.</summary>
    /// <returns>A dictionary of cache keys mapped to their read result envelopes.</returns>
    ValueTask<IDictionary<string, CacheValue<T>>> GetAllAsync<T>(
        IEnumerable<string> cacheKeys,
        CancellationToken cancellationToken = default
    );

    /// <summary>
    /// Reads all entries whose key starts with <paramref name="prefix"/>. Excludes logically expired and tag-invalidated entries.
    /// </summary>
    /// <returns>A dictionary of cache keys mapped to hits.</returns>
    ValueTask<IDictionary<string, CacheValue<T>>> GetByPrefixAsync<T>(
        string prefix,
        CancellationToken cancellationToken = default
    );

    /// <summary>Returns all keys that start with <paramref name="prefix"/>, including expired entries.</summary>
    ValueTask<IReadOnlyList<string>> GetAllKeysByPrefixAsync(
        string prefix,
        CancellationToken cancellationToken = default
    );

    /// <summary>
    /// Gets the value for <paramref name="key"/>. Returns <see cref="CacheValue{T}.NoValue"/> on miss, logical expiry, or tag invalidation.
    /// </summary>
    ValueTask<CacheValue<T>> GetAsync<T>(string key, CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns the count of non-expired entries, optionally filtered by key prefix.
    /// </summary>
    ValueTask<long> GetCountAsync(string prefix = "", CancellationToken cancellationToken = default);

    /// <summary>Returns <see langword="true"/> when <paramref name="key"/> exists and is not logically expired or tag-invalidated.</summary>
    ValueTask<bool> ExistsAsync(string key, CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns the remaining logical expiration time for <paramref name="key"/>, or <see langword="null"/> when missing or expired.
    /// </summary>
    ValueTask<TimeSpan?> GetExpirationAsync(string key, CancellationToken cancellationToken = default);

    /// <summary>
    /// Reads a page of members from the set stored at <paramref name="key"/>. Excludes individually expired members.
    /// </summary>
    ValueTask<CacheValue<ICollection<T>>> GetSetAsync<T>(
        string key,
        int? pageIndex = null,
        int pageSize = 100,
        CancellationToken cancellationToken = default
    );

    /// <summary>
    /// Extends the idle window of a sliding cache entry without materializing its value.
    /// </summary>
    /// <param name="key">The cache key.</param>
    /// <param name="cancellationToken">A token to monitor for cancellation requests.</param>
    ValueTask RefreshAsync(string key, CancellationToken cancellationToken = default);

    #endregion

    #region Remove

    /// <summary>Removes the specified cache key.</summary>
    ValueTask<bool> RemoveAsync(string key, CancellationToken cancellationToken = default);

    /// <summary>
    /// Logically expires an entry while preserving its fail-safe reserve.
    /// </summary>
    /// <param name="key">The cache key to logically expire.</param>
    /// <param name="cancellationToken">A token to monitor for cancellation requests.</param>
    /// <returns><see langword="true"/> when an entry was found and expired; otherwise, <see langword="false"/>.</returns>
    ValueTask<bool> ExpireAsync(string key, CancellationToken cancellationToken = default);

    /// <summary>
    /// Removes <paramref name="key"/> only when the current stored value equals <paramref name="expected"/>.
    /// </summary>
    ValueTask<bool> RemoveIfEqualAsync<T>(string key, T? expected, CancellationToken cancellationToken = default);

    /// <summary>Removes all specified keys and returns the number of removed entries.</summary>
    ValueTask<int> RemoveAllAsync(IEnumerable<string> cacheKeys, CancellationToken cancellationToken = default);

    /// <summary>Removes all keys that start with <paramref name="prefix"/> and returns the number of removed entries.</summary>
    ValueTask<int> RemoveByPrefixAsync(string prefix, CancellationToken cancellationToken = default);

    /// <summary>
    /// Logically invalidates every entry that carries <paramref name="tag"/> by advancing a tag invalidation marker.
    /// </summary>
    /// <param name="tag">The invalidation tag.</param>
    /// <param name="cancellationToken">A token to monitor for cancellation requests.</param>
    ValueTask RemoveByTagAsync(string tag, CancellationToken cancellationToken = default);

    /// <summary>
    /// Logically clears the cache by advancing a clear generation marker while preserving fail-safe reserves.
    /// </summary>
    /// <param name="cancellationToken">A token to monitor for cancellation requests.</param>
    ValueTask ClearAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Removes the specified members from the set stored at <paramref name="key"/>.
    /// </summary>
    ValueTask<long> SetRemoveAsync<T>(
        string key,
        IEnumerable<T> value,
        TimeSpan? expiration,
        CancellationToken cancellationToken = default
    );

    /// <summary>
    /// Flushes the cache and removes all entries including fail-safe reserves.
    /// </summary>
    /// <param name="cancellationToken">A token to monitor for cancellation requests.</param>
    ValueTask FlushAsync(CancellationToken cancellationToken = default);

    #endregion
}

/// <summary>
/// Defines the in-memory process-local L1 cache tier marker interface.
/// </summary>
[PublicAPI]
public interface IInMemoryCache : ICache;

/// <summary>
/// Defines the distributed remote L2 cache tier contract. Extends <see cref="ICache"/> with single round-trip
/// value and expiration reads.
/// </summary>
[PublicAPI]
public interface IRemoteCache : ICache
{
    /// <summary>
    /// Reads a key in one round-trip and returns the value with its remaining logical expiration.
    /// </summary>
    /// <typeparam name="T">The type of the cached value.</typeparam>
    /// <param name="key">The cache key to look up.</param>
    /// <param name="cancellationToken">A token to monitor for cancellation requests.</param>
    /// <returns>
    /// A <see cref="CacheValueWithExpiration{T}"/> containing the value and remaining expiration when found.
    /// </returns>
    ValueTask<CacheValueWithExpiration<T>> GetWithExpirationAsync<T>(
        string key,
        CancellationToken cancellationToken = default
    );

    /// <summary>
    /// Reads multiple keys in one round-trip and returns each hit with its remaining logical expiration.
    /// </summary>
    /// <typeparam name="T">The type of the cached values.</typeparam>
    /// <param name="cacheKeys">The keys to look up.</param>
    /// <param name="cancellationToken">A token to monitor for cancellation requests.</param>
    /// <returns>
    /// A dictionary keyed by cache key containing the value and remaining logical expiration for found entries.
    /// </returns>
    ValueTask<IDictionary<string, CacheValueWithExpiration<T>>> GetAllWithExpirationAsync<T>(
        IEnumerable<string> cacheKeys,
        CancellationToken cancellationToken = default
    );
}
