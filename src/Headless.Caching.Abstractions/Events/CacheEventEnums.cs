// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Caching;

#pragma warning disable MA0048 // A topic file: its types are peers with no main type, so the file is named for the topic.
/// <summary>Identifies the cache tier that raised an event. Aligns with the <c>headless.cache.tier</c> metric dimension.</summary>
[PublicAPI]
public enum CacheTier
{
    /// <summary>Identifies the in-memory, process-local tier (L1).</summary>
    L1,

    /// <summary>Identifies the distributed, remote tier (L2).</summary>
    L2,

    /// <summary>Identifies the two-tier hybrid cache.</summary>
    Hybrid,
}

/// <summary>Identifies why an entry was evicted from the in-memory tier. Aligns with the <c>headless.cache.evict_reason</c> metric dimension.</summary>
[PublicAPI]
public enum CacheEvictionReason
{
    /// <summary>The entry physical lifetime expired.</summary>
    Expired,

    /// <summary>The entry was removed to reclaim memory under a capacity cap.</summary>
    Capacity,

    /// <summary>The entry was removed by an explicit removal call.</summary>
    Removed,

    /// <summary>The entry was dropped during a cache flush.</summary>
    Flushed,
}

/// <summary>Identifies what triggered fail-safe activation. Aligns with the <c>headless.cache.trigger</c> metric dimension.</summary>
[PublicAPI]
public enum CacheFailSafeTrigger
{
    /// <summary>The factory threw an exception.</summary>
    FactoryError,

    /// <summary>The factory exceeded a soft or hard timeout.</summary>
    FactoryTimeout,

    /// <summary>Acquiring the distributed factory lock failed.</summary>
    LockAcquireFailed,
}

/// <summary>Identifies the kind of refresh executed outside the request path. Aligns with the <c>headless.cache.refresh_kind</c> metric dimension.</summary>
[PublicAPI]
public enum CacheRefreshKind
{
    /// <summary>An eager refresh triggered by a hit past the eager refresh threshold.</summary>
    Eager,

    /// <summary>A background completion of a factory after a soft timeout.</summary>
    Background,
}

/// <summary>Identifies the outcome of a factory execution or refresh. Aligns with the <c>headless.cache.outcome</c> metric dimension.</summary>
[PublicAPI]
public enum CacheFactoryOutcome
{
    /// <summary>The factory completed successfully.</summary>
    Success,

    /// <summary>The factory threw an exception.</summary>
    Error,

    /// <summary>The factory timed out.</summary>
    Timeout,
}

/// <summary>Identifies the kind of hybrid invalidation. Aligns with the <c>headless.cache.invalidation_kind</c> metric dimension.</summary>
[PublicAPI]
public enum CacheInvalidationKind
{
    /// <summary>A tag invalidation operation.</summary>
    Tag,

    /// <summary>A logical cache clear operation.</summary>
    Clear,

    /// <summary>A cache flush operation.</summary>
    Flush,
}

/// <summary>Identifies the direction of hybrid invalidation propagation. Aligns with the <c>headless.cache.direction</c> metric dimension.</summary>
[PublicAPI]
public enum CacheInvalidationDirection
{
    /// <summary>This instance published the invalidation to peers.</summary>
    Publish,

    /// <summary>This instance received the invalidation from a peer.</summary>
    Receive,
}
