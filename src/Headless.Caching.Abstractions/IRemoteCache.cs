// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Caching;

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
