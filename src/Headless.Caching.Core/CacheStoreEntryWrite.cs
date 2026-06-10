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

    /// <summary>Gets the optional invalidation tags associated with the cached value.</summary>
    public IReadOnlyCollection<string>? Tags { get; init; }

    /// <summary>
    /// Gets the tags that were present on the previous physically-retained entry but are absent from this write.
    /// Stores that maintain an external reverse tag index (e.g. Redis) drop these stale memberships atomically
    /// with the write; stores with the old entry at hand in-process (in-memory) may ignore it.
    /// </summary>
    public IReadOnlyCollection<string>? RemovedTags { get; init; }
}
