// Copyright (c) Mahmoud Shaheen. All rights reserved.

#pragma warning disable IDE0130 // ReSharper disable once CheckNamespace
namespace Headless.Caching;

/// <summary>Store primitive used by <see cref="FactoryCacheCoordinator"/>.</summary>
[PublicAPI]
public interface IFactoryCacheStore
{
    /// <summary>Attempts to get an entry with its logical and physical expiration metadata.</summary>
    /// <remarks>
    /// Implementations must return entries that are still physically present even when logical expiration has
    /// passed. The coordinator uses that state as a fail-safe stale candidate. Return
    /// <see cref="CacheStoreEntry{T}.NotFound"/> only when the entry is missing or physically expired.
    /// Expiration timestamps are UTC. When <see cref="CacheStoreEntry{T}.IsNull"/> is <see langword="true"/>,
    /// <see cref="CacheStoreEntry{T}.Value"/> is ignored and the coordinator returns a cached null value.
    /// </remarks>
    /// <typeparam name="T">The cached value type.</typeparam>
    /// <param name="key">The cache key.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    ValueTask<CacheStoreEntry<T>> TryGetEntryAsync<T>(string key, CancellationToken cancellationToken);

    /// <summary>Sets an entry with explicit logical and physical expiration metadata.</summary>
    /// <typeparam name="T">The cached value type.</typeparam>
    /// <param name="key">The cache key.</param>
    /// <param name="value">The cached value.</param>
    /// <param name="isNull">Whether the stored value is the cache null sentinel.</param>
    /// <param name="logicalExpiresAt">The logical expiration timestamp.</param>
    /// <param name="physicalExpiresAt">The physical expiration timestamp.</param>
    /// <param name="slidingExpiration">The optional idle window used to re-arm logical expiration.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    ValueTask SetEntryAsync<T>(
        string key,
        T? value,
        bool isNull,
        DateTime logicalExpiresAt,
        DateTime physicalExpiresAt,
        TimeSpan? slidingExpiration,
        CancellationToken cancellationToken
    );

    /// <summary>
    /// Re-arms a sliding entry's logical expiration on a fresh read, extending it by the idle window without
    /// rewriting the stored value.
    /// </summary>
    /// <remarks>
    /// This is a metadata-only TTL bump, not a value write: implementations must extend the existing entry in
    /// place (Redis <c>KeyExpire</c>, in-memory logical-expiration swap) rather than re-encode and re-store the
    /// payload. Implementations must (a) only ever extend, never shorten, the entry's lifetime, (b) cap the new
    /// logical deadline at <paramref name="physicalExpiresAt"/>, (c) throttle so a hot key is not re-armed on
    /// every read (re-arm only once roughly half the idle window has elapsed), and (d) treat the call as
    /// best-effort — a re-arm failure must not surface to the caller, because the value read already succeeded.
    /// It is a no-op when the entry is missing or already past its physical cap.
    /// </remarks>
    /// <param name="key">The cache key.</param>
    /// <param name="slidingExpiration">The idle window to extend logical expiration by.</param>
    /// <param name="physicalExpiresAt">The physical (retention) cap the re-armed logical deadline must not exceed.</param>
    /// <param name="now">
    /// The reference UTC time for the re-arm, supplied by the caller so it matches the freshness check that
    /// preceded it. Implementations use it to compute the new logical deadline and the throttle decision.
    /// </param>
    /// <param name="cancellationToken">The cancellation token.</param>
    ValueTask TryRearmSlidingAsync(
        string key,
        TimeSpan slidingExpiration,
        DateTime physicalExpiresAt,
        DateTime now,
        CancellationToken cancellationToken
    );
}
