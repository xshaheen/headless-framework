// Copyright (c) Mahmoud Shaheen. All rights reserved.

#pragma warning disable IDE0130 // ReSharper disable once CheckNamespace
namespace Headless.Caching;

/// <summary>Provider entry snapshot used by the factory cache coordinator.</summary>
/// <typeparam name="T">The cached value type.</typeparam>
/// <param name="Found">Whether the store contains an entry.</param>
/// <param name="IsNull">Whether the stored value is the cache null sentinel.</param>
/// <param name="Value">The cached value.</param>
/// <param name="LogicalExpiresAt">The timestamp after which normal reads treat the entry as stale.</param>
/// <param name="PhysicalExpiresAt">The timestamp after which the entry is no longer retained.</param>
/// <param name="SlidingExpiration">The optional idle window used to re-arm logical expiration on value reads.</param>
[PublicAPI]
public readonly record struct CacheStoreEntry<T>(
    bool Found,
    bool IsNull,
    T? Value,
    DateTime? LogicalExpiresAt,
    DateTime? PhysicalExpiresAt,
    TimeSpan? SlidingExpiration
)
{
    /// <summary>Gets the optional timestamp after which a fresh read may trigger an eager background refresh.</summary>
    public DateTime? EagerRefreshAt { get; init; }

    /// <summary>Gets the optional opaque entity tag the factory associated with the cached value.</summary>
    public string? ETag { get; init; }

    /// <summary>Gets the optional timestamp at which the cached value was last modified at its origin.</summary>
    public DateTime? LastModifiedAt { get; init; }

    /// <summary>
    /// Gets the optional UTC timestamp at which this entry's value was first created (its birth time). A re-stamp
    /// (a conditional <c>NotModified</c> extension or a fail-safe throttle restamp) preserves the original
    /// <see cref="CreatedAt"/>; only a genuine new value write sets it afresh. <see langword="null"/> for legacy
    /// or unframed entries written before the timestamp existed. No read-time verdict consumes it yet.
    /// </summary>
    public DateTime? CreatedAt { get; init; }

    /// <summary>Gets the optional invalidation tags associated with the cached value.</summary>
    public IReadOnlyCollection<string>? Tags { get; init; }

    /// <summary>
    /// Gets an opaque store-owned stamp identifying the exact physical entry snapshot that was read.
    /// </summary>
    /// <remarks>
    /// The coordinator copies this stamp to <see cref="CacheStoreEntryWrite{T}.ExpectedConcurrencyStamp"/> for
    /// factory writes derived from an existing physical entry, so a late factory cannot resurrect a removed
    /// entry or clobber a concurrent writer. The value is provider-specific and must only be treated as an
    /// equality token.
    /// </remarks>
    public string? ConcurrencyStamp { get; init; }

    /// <summary>
    /// Gets whether the store is asking the coordinator to serve this physically-present stale entry without
    /// running the factory because a lower tier degraded during the read.
    /// </summary>
    public bool ServeStaleImmediately { get; init; }

    /// <summary>Gets an entry representing a store miss.</summary>
#pragma warning disable CA1000 // Do not declare static members on generic types
    public static CacheStoreEntry<T> NotFound { get; } =
#pragma warning restore CA1000
        new(
            Found: false,
            IsNull: false,
            Value: default,
            LogicalExpiresAt: null,
            PhysicalExpiresAt: null,
            SlidingExpiration: null
        );
}

/// <summary>Shared expiration predicates over <see cref="CacheStoreEntry{T}"/> used by Core and providers.</summary>
[PublicAPI]
public static class CacheStoreEntryExtensions
{
    /// <summary>Returns whether the entry is present and not past its logical (stale) expiration.</summary>
    /// <typeparam name="T">The cached value type.</typeparam>
    /// <param name="entry">The entry snapshot to evaluate.</param>
    /// <param name="now">The current UTC timestamp (from <see cref="TimeProvider.GetUtcNow"/>); expirations are UTC.</param>
    /// <returns><see langword="true"/> when the entry is physically present and not logically expired.</returns>
    public static bool IsFresh<T>(this CacheStoreEntry<T> entry, DateTime now)
    {
        if (!entry.IsPhysicallyPresent(now))
        {
            return false;
        }

        return !entry.LogicalExpiresAt.HasValue || entry.LogicalExpiresAt.Value > now;
    }

    /// <summary>Returns whether the entry is present and not past its physical (retention) expiration.</summary>
    /// <typeparam name="T">The cached value type.</typeparam>
    /// <param name="entry">The entry snapshot to evaluate.</param>
    /// <param name="now">The current UTC timestamp (from <see cref="TimeProvider.GetUtcNow"/>); expirations are UTC.</param>
    /// <returns><see langword="true"/> when the entry is found and not physically expired.</returns>
    public static bool IsPhysicallyPresent<T>(this CacheStoreEntry<T> entry, DateTime now)
    {
        return entry.Found && (!entry.PhysicalExpiresAt.HasValue || entry.PhysicalExpiresAt.Value > now);
    }
}
