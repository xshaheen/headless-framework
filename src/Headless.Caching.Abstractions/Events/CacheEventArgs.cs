// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Caching;

/// <summary>
/// Base arguments for every cache event. Carries the cache-instance identity but no key, so cache-wide operations
/// (clear, flush, tag/prefix removal, hybrid invalidation) can share the same base as keyed events.
/// </summary>
/// <remarks>
/// The key-bearing events derive from <see cref="CacheKeyEventArgs"/>. All keys are the caller-facing
/// keys the <see cref="ICache"/> API accepts and returns: the provider's internal <c>KeyPrefix</c> is stripped before the
/// args are constructed.
/// </remarks>
[PublicAPI]
public class CacheEventArgs(string cacheName, CacheTier tier) : EventArgs
{
    /// <summary>The registered cache-instance name (or <c>"default"</c> for the unkeyed default cache).</summary>
    public string CacheName { get; } = cacheName;

    /// <summary>The tier of the cache instance that raised the event.</summary>
    public CacheTier Tier { get; } = tier;
}

/// <summary>Base arguments for events that concern a single cache entry, carrying its caller-facing key.</summary>
[PublicAPI]
public class CacheKeyEventArgs(string cacheName, CacheTier tier, string key) : CacheEventArgs(cacheName, tier)
{
    /// <summary>The caller-facing cache key the event concerns (never the internally-prefixed store key).</summary>
    public string Key { get; } = key;
}

/// <summary>Arguments for a cache hit, with a flag distinguishing a fresh hit from a fail-safe stale serve.</summary>
[PublicAPI]
public sealed class CacheHitEventArgs(string cacheName, CacheTier tier, string key, bool isStale)
    : CacheKeyEventArgs(cacheName, tier, key)
{
    /// <summary>Gets a value indicating whether the served value was a fail-safe stale reserve rather than a fresh entry.</summary>
    public bool IsStale { get; } = isStale;
}

/// <summary>Provides event arguments for an in-memory eviction, including the eviction reason.</summary>
[PublicAPI]
public sealed class CacheEvictionEventArgs(string cacheName, CacheTier tier, string key, CacheEvictionReason reason)
    : CacheKeyEventArgs(cacheName, tier, key)
{
    /// <summary>Gets the reason why the entry was evicted.</summary>
    public CacheEvictionReason Reason { get; } = reason;
}

/// <summary>Provides event arguments for factory execution outcomes.</summary>
[PublicAPI]
public sealed class CacheFactoryEventArgs(string cacheName, CacheTier tier, string key, CacheFactoryOutcome outcome)
    : CacheKeyEventArgs(cacheName, tier, key)
{
    /// <summary>Gets the outcome of the factory execution.</summary>
    public CacheFactoryOutcome Outcome { get; } = outcome;
}

/// <summary>Provides event arguments for fail-safe stale-serving activation.</summary>
[PublicAPI]
public sealed class CacheFailSafeEventArgs(string cacheName, CacheTier tier, string key, CacheFailSafeTrigger trigger)
    : CacheKeyEventArgs(cacheName, tier, key)
{
    /// <summary>Gets the condition that triggered fail-safe activation.</summary>
    public CacheFailSafeTrigger Trigger { get; } = trigger;
}

/// <summary>Provides event arguments for an eager or background refresh, including kind and outcome.</summary>
[PublicAPI]
public sealed class CacheRefreshEventArgs(
    string cacheName,
    CacheTier tier,
    string key,
    CacheRefreshKind kind,
    CacheFactoryOutcome outcome
) : CacheKeyEventArgs(cacheName, tier, key)
{
    /// <summary>Gets a value indicating whether the refresh was eager or a background completion.</summary>
    public CacheRefreshKind Kind { get; } = kind;

    /// <summary>Gets the outcome of the refresh factory execution.</summary>
    public CacheFactoryOutcome Outcome { get; } = outcome;
}

/// <summary>Provides event arguments for prefix-scoped removals, including the prefix and removed count.</summary>
[PublicAPI]
public sealed class CacheRemoveByPrefixEventArgs(string cacheName, CacheTier tier, string prefix, int removedCount)
    : CacheEventArgs(cacheName, tier)
{
    /// <summary>Gets the key prefix targeted by the removal operation.</summary>
    public string Prefix { get; } = prefix;

    /// <summary>Gets the number of entries removed.</summary>
    public int RemovedCount { get; } = removedCount;
}

/// <summary>Provides event arguments for tag invalidation operations.</summary>
[PublicAPI]
public sealed class CacheRemoveByTagEventArgs(string cacheName, CacheTier tier, string tag)
    : CacheEventArgs(cacheName, tier)
{
    /// <summary>Gets the invalidation tag.</summary>
    public string Tag { get; } = tag;
}

/// <summary>Provides event arguments for bulk removal operations, including the count of removed entries.</summary>
[PublicAPI]
public sealed class CacheRemoveAllEventArgs(string cacheName, CacheTier tier, int removedCount)
    : CacheEventArgs(cacheName, tier)
{
    /// <summary>Gets the number of entries removed.</summary>
    public int RemovedCount { get; } = removedCount;
}

/// <summary>Provides event arguments for hybrid invalidation propagation.</summary>
[PublicAPI]
public sealed class CacheInvalidationEventArgs(
    string cacheName,
    CacheTier tier,
    CacheInvalidationKind kind,
    CacheInvalidationDirection direction,
    string? tag = null
) : CacheEventArgs(cacheName, tier)
{
    /// <summary>Gets the invalidation kind.</summary>
    public CacheInvalidationKind Kind { get; } = kind;

    /// <summary>Gets a value indicating whether this instance published or received the invalidation.</summary>
    public CacheInvalidationDirection Direction { get; } = direction;

    /// <summary>Gets the tag for tag invalidations, or <see langword="null"/> for clear or flush operations.</summary>
    public string? Tag { get; } = tag;
}
