// Copyright (c) Mahmoud Shaheen. All rights reserved.

#pragma warning disable IDE0130 // ReSharper disable once CheckNamespace
namespace Headless.Caching;

/// <summary>
/// Base arguments for every cache event. Carries the cache-instance identity but no key, so cache-wide operations
/// (clear, flush, tag/prefix removal, hybrid invalidation) can share the same base as keyed events.
/// </summary>
/// <remarks>
/// The key-bearing events derive from <see cref="CacheKeyEventArgs"/>. All keys are the caller-facing
/// keys the <c>ICache</c> API accepts and returns — the provider's internal <c>KeyPrefix</c> is stripped before the
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
    /// <summary>Whether the served value was a fail-safe stale reserve rather than a fresh entry.</summary>
    public bool IsStale { get; } = isStale;
}

/// <summary>Arguments for an in-memory eviction, carrying the reason the entry left the tier.</summary>
[PublicAPI]
public sealed class CacheEvictionEventArgs(string cacheName, CacheTier tier, string key, CacheEvictionReason reason)
    : CacheKeyEventArgs(cacheName, tier, key)
{
    /// <summary>Why the entry was evicted.</summary>
    public CacheEvictionReason Reason { get; } = reason;
}

/// <summary>Arguments for a factory execution outcome (success, error, or timeout).</summary>
[PublicAPI]
public sealed class CacheFactoryEventArgs(string cacheName, CacheTier tier, string key, CacheFactoryOutcome outcome)
    : CacheKeyEventArgs(cacheName, tier, key)
{
    /// <summary>The factory's outcome.</summary>
    public CacheFactoryOutcome Outcome { get; } = outcome;
}

/// <summary>Arguments for a fail-safe stale-serving activation.</summary>
[PublicAPI]
public sealed class CacheFailSafeEventArgs(string cacheName, CacheTier tier, string key, CacheFailSafeTrigger trigger)
    : CacheKeyEventArgs(cacheName, tier, key)
{
    /// <summary>What triggered the activation.</summary>
    public CacheFailSafeTrigger Trigger { get; } = trigger;
}

/// <summary>Arguments for an eager or background refresh, carrying its kind and outcome.</summary>
[PublicAPI]
public sealed class CacheRefreshEventArgs(
    string cacheName,
    CacheTier tier,
    string key,
    CacheRefreshKind kind,
    CacheFactoryOutcome outcome
) : CacheKeyEventArgs(cacheName, tier, key)
{
    /// <summary>Whether the refresh was eager or a background completion.</summary>
    public CacheRefreshKind Kind { get; } = kind;

    /// <summary>The refresh factory's outcome.</summary>
    public CacheFactoryOutcome Outcome { get; } = outcome;
}

/// <summary>Arguments for a prefix-scoped removal, carrying the prefix and the number of entries removed.</summary>
[PublicAPI]
public sealed class CacheRemoveByPrefixEventArgs(string cacheName, CacheTier tier, string prefix, int removedCount)
    : CacheEventArgs(cacheName, tier)
{
    /// <summary>The key prefix the removal targeted (caller-facing).</summary>
    public string Prefix { get; } = prefix;

    /// <summary>The number of entries removed.</summary>
    public int RemovedCount { get; } = removedCount;
}

/// <summary>Arguments for a tag invalidation, carrying the tag. Tag invalidation is an O(1) marker bump that knows no keys.</summary>
[PublicAPI]
public sealed class CacheRemoveByTagEventArgs(string cacheName, CacheTier tier, string tag)
    : CacheEventArgs(cacheName, tier)
{
    /// <summary>The invalidation tag.</summary>
    public string Tag { get; } = tag;
}

/// <summary>Arguments for a bulk <c>RemoveAllAsync</c>, carrying the number of entries removed.</summary>
[PublicAPI]
public sealed class CacheRemoveAllEventArgs(string cacheName, CacheTier tier, int removedCount)
    : CacheEventArgs(cacheName, tier)
{
    /// <summary>The number of entries removed.</summary>
    public int RemovedCount { get; } = removedCount;
}

/// <summary>Arguments for a hybrid invalidation propagation, carrying its kind, direction, and (for tag kind) the tag.</summary>
[PublicAPI]
public sealed class CacheInvalidationEventArgs(
    string cacheName,
    CacheTier tier,
    CacheInvalidationKind kind,
    CacheInvalidationDirection direction,
    string? tag = null
) : CacheEventArgs(cacheName, tier)
{
    /// <summary>The invalidation kind.</summary>
    public CacheInvalidationKind Kind { get; } = kind;

    /// <summary>Whether this instance published or received the invalidation.</summary>
    public CacheInvalidationDirection Direction { get; } = direction;

    /// <summary>The tag for a <see cref="CacheInvalidationKind.Tag"/> invalidation; <see langword="null"/> for clear/flush.</summary>
    public string? Tag { get; } = tag;
}
