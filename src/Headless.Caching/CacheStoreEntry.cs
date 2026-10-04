// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Caching;

/// <summary>Represents a cache store entry snapshot used by the factory cache coordinator.</summary>
/// <typeparam name="T">The cached value type.</typeparam>
/// <param name="Found">Indicates whether the store contains an entry.</param>
/// <param name="IsNull">Indicates whether the stored value represents a cached null value.</param>
/// <param name="Value">The cached value.</param>
/// <param name="LogicalExpiresAt">The timestamp after which normal reads treat the entry as stale (UTC).</param>
/// <param name="PhysicalExpiresAt">The timestamp after which the entry is no longer retained (UTC).</param>
/// <param name="SlidingExpiration">The optional idle window used to extend logical expiration on value reads.</param>
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
    /// <summary>Gets the optional timestamp after which a fresh read can trigger an eager background refresh (UTC).</summary>
    public DateTime? EagerRefreshAt { get; init; }

    /// <summary>Gets the optional opaque entity tag associated with the cached value.</summary>
    public string? ETag { get; init; }

    /// <summary>Gets the optional origin timestamp at which the cached value was last modified.</summary>
    public DateTime? LastModifiedAt { get; init; }

    /// <summary>
    /// Gets the optional UTC timestamp at which this value was first created.
    /// </summary>
    public DateTime? CreatedAt { get; init; }

    /// <summary>Gets the optional invalidation tags associated with the cached value.</summary>
    public IReadOnlyCollection<string>? Tags { get; init; }

    /// <summary>
    /// Gets an opaque stamp identifying the exact physical entry snapshot that was read.
    /// </summary>
    /// <remarks>
    /// The coordinator copies this stamp to <see cref="CacheStoreEntryWrite{T}.ExpectedConcurrencyStamp"/> for
    /// writes derived from an existing entry to guard against concurrent writes.
    /// </remarks>
    public string? ConcurrencyStamp { get; init; }

    /// <summary>
    /// Gets a value indicating whether the store requests serving this stale entry without executing the factory.
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

/// <summary>Provides expiration predicates over <see cref="CacheStoreEntry{T}"/>.</summary>
[PublicAPI]
public static class CacheStoreEntryExtensions
{
    /// <summary>Determines whether the entry is present and logically fresh.</summary>
    /// <typeparam name="T">The cached value type.</typeparam>
    /// <param name="entry">The entry snapshot to evaluate.</param>
    /// <param name="now">The current UTC timestamp.</param>
    /// <returns><see langword="true"/> when the entry is physically present and not logically expired; otherwise, <see langword="false"/>.</returns>
    public static bool IsFresh<T>(this CacheStoreEntry<T> entry, DateTime now)
    {
        if (!entry.IsPhysicallyPresent(now))
        {
            return false;
        }

        return !entry.LogicalExpiresAt.HasValue || entry.LogicalExpiresAt.Value > now;
    }

    /// <summary>Determines whether the entry is physically retained in the store.</summary>
    /// <typeparam name="T">The cached value type.</typeparam>
    /// <param name="entry">The entry snapshot to evaluate.</param>
    /// <param name="now">The current UTC timestamp.</param>
    /// <returns><see langword="true"/> when the entry is found and not physically expired; otherwise, <see langword="false"/>.</returns>
    public static bool IsPhysicallyPresent<T>(this CacheStoreEntry<T> entry, DateTime now)
    {
        return entry.Found && (!entry.PhysicalExpiresAt.HasValue || entry.PhysicalExpiresAt.Value > now);
    }
}
