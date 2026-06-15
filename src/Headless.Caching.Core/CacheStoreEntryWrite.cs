// Copyright (c) Mahmoud Shaheen. All rights reserved.

#pragma warning disable IDE0130 // ReSharper disable once CheckNamespace
namespace Headless.Caching;

/// <summary>Write descriptor passed to <see cref="IFactoryCacheStore.SetEntryAsync{T}"/>.</summary>
/// <typeparam name="T">The cached value type.</typeparam>
[PublicAPI]
public readonly record struct CacheStoreEntryWrite<T>
{
    /// <summary>Gets the cached value.</summary>
    public required T? Value { get; init; }

    /// <summary>Gets whether the stored value is the cache null sentinel.</summary>
    public required bool IsNull { get; init; }

    /// <summary>Gets the logical expiration timestamp (UTC).</summary>
    public required DateTime LogicalExpiresAt { get; init; }

    /// <summary>Gets the physical (retention) expiration timestamp (UTC).</summary>
    public required DateTime PhysicalExpiresAt { get; init; }

    /// <summary>Gets the optional idle window used to re-arm logical expiration.</summary>
    public TimeSpan? SlidingExpiration { get; init; }

    /// <summary>Gets the optional timestamp after which a fresh read may trigger an eager background refresh.</summary>
    public DateTime? EagerRefreshAt { get; init; }

    /// <summary>Gets the optional opaque entity tag the factory associated with the cached value.</summary>
    public string? ETag { get; init; }

    /// <summary>Gets the optional timestamp at which the cached value was last modified at its origin.</summary>
    public DateTime? LastModifiedAt { get; init; }

    /// <summary>
    /// Gets the optional UTC timestamp at which this entry's value was created (its birth time). A genuine new
    /// value write sets it to "now"; a re-stamp (<see cref="IsRestamp"/>) carries the source entry's original
    /// <see cref="CreatedAt"/> forward so the birth time survives <c>NotModified</c> extensions and fail-safe
    /// throttle restamps. Declared as an initializer-only nullable property (not <c>required</c>) so existing
    /// construction sites that do not set it stay valid and persist <see langword="null"/>. No read-time verdict
    /// consumes it yet.
    /// </summary>
    public DateTime? CreatedAt { get; init; }

    /// <summary>Gets the optional invalidation tags associated with the cached value.</summary>
    public IReadOnlyCollection<string>? Tags { get; init; }

    /// <summary>
    /// Gets an opaque store-owned stamp the live entry must still match for this write to commit.
    /// </summary>
    /// <remarks>
    /// <see langword="null"/> means unconditional write. Factory refresh paths set this from the
    /// <see cref="CacheStoreEntry{T}.ConcurrencyStamp"/> they read before running the factory, turning the final
    /// write into a compare-and-set so a concurrent remove or value write wins over the late factory result.
    /// </remarks>
    public string? ExpectedConcurrencyStamp { get; init; }

    /// <summary>
    /// Gets whether this write merely re-stamps the value already cached under the key with new expiration
    /// metadata (a conditional <c>NotModified</c> extension, a fail-safe throttle restamp, or an eager-refresh
    /// gate write) instead of producing a new value. Multi-tier stores use this to skip cross-instance
    /// invalidation: peers' cached bytes are still identical, so invalidating them would only force pointless
    /// remote re-reads. Defaults to <see langword="false"/> (a value-producing write).
    /// </summary>
    public bool IsRestamp { get; init; }

    /// <summary>
    /// Gets whether the L1 (memory) tier write must be skipped for this entry. Hybrid-relevant only: single-tier
    /// stores ignore it. The coordinator copies this from <see cref="CacheEntryOptions.SkipMemoryCacheWrite"/>.
    /// Declared as an initializer-only property (not a positional parameter) so existing construction sites stay
    /// valid. Defaults to <see langword="false"/>.
    /// </summary>
    public bool SkipMemoryCacheWrite { get; init; }

    /// <summary>
    /// Gets whether the L2 (distributed) tier write must be skipped for this entry. Hybrid-relevant only:
    /// single-tier stores ignore it. The coordinator copies this from
    /// <see cref="CacheEntryOptions.SkipDistributedCacheWrite"/>. Declared as an initializer-only property (not a
    /// positional parameter) so existing construction sites stay valid. Defaults to <see langword="false"/>.
    /// </summary>
    public bool SkipDistributedCacheWrite { get; init; }
}
