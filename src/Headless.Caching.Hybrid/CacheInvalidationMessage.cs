// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Caching;

/// <summary>
/// Message published over <c>IBus</c> by <see cref="HybridCache"/> to notify peer instances to evict specific
/// L1 entries. Exactly one invalidation target must be set per message: <see cref="Key"/>, <see cref="Keys"/>,
/// <see cref="Prefix"/>, <see cref="Tag"/>, <see cref="Clear"/>, or <see cref="FlushAll"/>.
/// </summary>
/// <remarks>
/// Consumers register <see cref="HybridCacheInvalidationConsumer"/> with Headless Messaging to receive these
/// messages. Messages whose <see cref="InstanceId"/> matches the receiving instance are silently ignored to
/// prevent echo invalidation. The <see cref="Timestamp"/> field is load-bearing for tag/clear/flush messages:
/// receivers seed their L1 marker from it so cross-node clock skew does not reintroduce stale entries.
/// </remarks>
[PublicAPI]
public sealed record CacheInvalidationMessage
{
    /// <summary>
    /// ID of the instance that originated this invalidation.
    /// Used to filter out self-originated messages to prevent echo.
    /// </summary>
    public required string InstanceId { get; init; }

    /// <summary>
    /// Optional named hybrid cache target. <see langword="null"/> targets the default hybrid cache instance.
    /// </summary>
    public string? CacheName { get; init; }

    /// <summary>Single key to invalidate. Mutually exclusive with <see cref="Prefix"/> and <see cref="FlushAll"/>.</summary>
    public string? Key { get; init; }

    /// <summary>Multiple keys to invalidate. Mutually exclusive with <see cref="Prefix"/> and <see cref="FlushAll"/>.</summary>
    public string[]? Keys { get; init; }

    /// <summary>Prefix for prefix-based invalidation. Mutually exclusive with <see cref="Key"/> and <see cref="FlushAll"/>.</summary>
    public string? Prefix { get; init; }

    /// <summary>Tag for tag-based invalidation (<see cref="ICache.RemoveByTagAsync"/>). Mutually exclusive with <see cref="Key"/>, <see cref="Keys"/>, <see cref="Prefix"/>, and <see cref="FlushAll"/>.</summary>
    public string? Tag { get; init; }

    /// <summary>
    /// When true, flush all cache entries on receivers, dropping fail-safe reserves (unlike <see cref="Clear"/>):
    /// receivers physically wipe their in-process (L1) cache and seed their distributed (L2) remove-generation
    /// marker from <see cref="Timestamp"/>, so entries born before it read as a hard miss with no reserve. Mutually
    /// exclusive with <see cref="Key"/> and <see cref="Prefix"/>.
    /// </summary>
    public bool FlushAll { get; init; }

    /// <summary>
    /// When true, LOGICALLY clear the cache on receivers by bumping their local clear-generation marker
    /// (<see cref="ICache.ClearAsync"/>): every entry born before the bump reads as a miss but its fail-safe
    /// physical reserve is preserved. Distinct from <see cref="FlushAll"/>, which drops reserves (a physical L1 wipe
    /// on receivers plus a logical L2 remove-generation marker). Mutually exclusive
    /// with <see cref="Key"/>, <see cref="Keys"/>, <see cref="Prefix"/>, <see cref="Tag"/>, and <see cref="FlushAll"/>.
    /// </summary>
    public bool Clear { get; init; }

    /// <summary>
    /// When true, the <see cref="Key"/>/<see cref="Keys"/> targets are LOGICALLY expired on receivers rather than
    /// removed: each entry becomes stale (normal reads miss) but its fail-safe physical reserve is preserved, so a
    /// later failing factory can still serve the stale value. Only meaningful with <see cref="Key"/> or
    /// <see cref="Keys"/> set; ignored for <see cref="Prefix"/>, <see cref="Tag"/>, and <see cref="FlushAll"/>.
    /// </summary>
    public bool Expire { get; init; }

    /// <summary>
    /// UTC timestamp at which the originating instance published this invalidation, stamped automatically by
    /// <see cref="HybridCache"/> on publish. Two uses: (1) auto-recovery conflict resolution — a queued write older
    /// than an incoming invalidation is dropped so replaying it cannot resurrect stale data; (2) for
    /// <see cref="Tag"/>, <see cref="Clear"/>, and <see cref="FlushAll"/> messages, receivers seed their L1 (and L2)
    /// marker from this origin timestamp rather than their own clock, so a receiver whose clock lags the origin
    /// still records a marker newer than the invalidated entries' birth time (closing the cross-node clock-skew
    /// window). When <see langword="null"/>, receivers fall back to their local clock, reintroducing that skew window.
    /// </summary>
    public DateTimeOffset? Timestamp { get; init; }
}
