// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Caching;

/// <summary>Represents a write descriptor passed to <see cref="IFactoryCacheStore.SetEntryAsync{T}"/>.</summary>
/// <typeparam name="T">The cached value type.</typeparam>
[PublicAPI]
public readonly record struct CacheStoreEntryWrite<T>
{
    /// <summary>Gets the cached value.</summary>
    public required T? Value { get; init; }

    /// <summary>Gets a value indicating whether the stored value represents a cached null sentinel.</summary>
    public required bool IsNull { get; init; }

    /// <summary>Gets the logical expiration timestamp (UTC).</summary>
    public required DateTime LogicalExpiresAt { get; init; }

    /// <summary>Gets the physical retention expiration timestamp (UTC).</summary>
    public required DateTime PhysicalExpiresAt { get; init; }

    /// <summary>Gets the optional idle window used to extend logical expiration.</summary>
    public TimeSpan? SlidingExpiration { get; init; }

    /// <summary>Gets the optional timestamp after which a fresh read can trigger an eager background refresh (UTC).</summary>
    public DateTime? EagerRefreshAt { get; init; }

    /// <summary>Gets the optional opaque entity tag associated with the cached value.</summary>
    public string? ETag { get; init; }

    /// <summary>Gets the optional origin timestamp at which the cached value was last modified.</summary>
    public DateTime? LastModifiedAt { get; init; }

    /// <summary>
    /// Gets the optional UTC timestamp at which this value was created.
    /// </summary>
    public DateTime? CreatedAt { get; init; }

    /// <summary>Gets the optional invalidation tags associated with the cached value.</summary>
    public IReadOnlyCollection<string>? Tags { get; init; }

    /// <summary>
    /// Gets an opaque stamp the live entry must match for this write to commit.
    /// </summary>
    /// <remarks>
    /// A value of <see langword="null"/> indicates an unconditional write.
    /// </remarks>
    public string? ExpectedConcurrencyStamp { get; init; }

    /// <summary>
    /// Gets a value indicating whether this write only restamps existing cached data with updated expiration metadata.
    /// </summary>
    public bool IsRestamp { get; init; }

    /// <summary>
    /// Gets a value indicating whether the L1 memory tier write must be skipped for this entry.
    /// </summary>
    public bool SkipMemoryCacheWrite { get; init; }

    /// <summary>
    /// Gets a value indicating whether the L2 distributed tier write must be skipped for this entry.
    /// </summary>
    public bool SkipDistributedCacheWrite { get; init; }
}
