// Copyright (c) Mahmoud Shaheen. All rights reserved.

#pragma warning disable IDE0130 // ReSharper disable once CheckNamespace
namespace Headless.Caching;

/// <summary>The cache tier that raised an event. Mirrors the <c>headless.cache.tier</c> metric dimension.</summary>
[PublicAPI]
public enum CacheTier
{
    /// <summary>The in-memory, process-local tier (L1).</summary>
    L1,

    /// <summary>The distributed, remote tier (L2).</summary>
    L2,

    /// <summary>The two-tier hybrid cache (L1 + L2).</summary>
    Hybrid,
}

/// <summary>Why an entry was evicted from the in-memory tier. Mirrors the <c>headless.cache.evict_reason</c> metric dimension.</summary>
[PublicAPI]
public enum CacheEvictionReason
{
    /// <summary>The entry's physical lifetime elapsed (maintenance sweep or lazy read-path reap).</summary>
    Expired,

    /// <summary>The entry was removed to reclaim memory under a size or count cap.</summary>
    Capacity,

    /// <summary>The entry was removed by an explicit remove operation.</summary>
    Removed,

    /// <summary>The entry was dropped by a whole-cache flush.</summary>
    Flushed,
}

/// <summary>What triggered a fail-safe stale-serving activation. Mirrors the <c>headless.cache.trigger</c> metric dimension.</summary>
[PublicAPI]
public enum CacheFailSafeTrigger
{
    /// <summary>The factory threw a non-timeout exception.</summary>
    FactoryError,

    /// <summary>The factory hit a soft or hard timeout.</summary>
    FactoryTimeout,

    /// <summary>Acquiring the distributed factory lock failed.</summary>
    LockAcquireFailed,
}

/// <summary>The kind of a refresh performed outside the caller's request path. Mirrors the <c>headless.cache.refresh_kind</c> metric dimension.</summary>
[PublicAPI]
public enum CacheRefreshKind
{
    /// <summary>An eager refresh started because a fresh hit passed the eager-refresh threshold.</summary>
    Eager,

    /// <summary>A background completion of a factory relegated after a soft timeout.</summary>
    Background,
}

/// <summary>The outcome of a factory execution or a refresh. Mirrors the <c>headless.cache.outcome</c> metric dimension.</summary>
[PublicAPI]
public enum CacheFactoryOutcome
{
    /// <summary>The factory completed successfully.</summary>
    Success,

    /// <summary>The factory threw.</summary>
    Error,

    /// <summary>The factory timed out.</summary>
    Timeout,
}

/// <summary>The kind of a hybrid invalidation. Mirrors the <c>headless.cache.invalidation_kind</c> metric dimension.</summary>
[PublicAPI]
public enum CacheInvalidationKind
{
    /// <summary>A tag invalidation (<c>RemoveByTagAsync</c>).</summary>
    Tag,

    /// <summary>A logical whole-cache clear (<c>ClearAsync</c>).</summary>
    Clear,

    /// <summary>A whole-cache flush (<c>FlushAsync</c>).</summary>
    Flush,
}

/// <summary>The direction of a hybrid invalidation relative to this instance. Mirrors the <c>headless.cache.direction</c> metric dimension.</summary>
[PublicAPI]
public enum CacheInvalidationDirection
{
    /// <summary>This instance published the invalidation to peers.</summary>
    Publish,

    /// <summary>This instance received the invalidation from a peer.</summary>
    Receive,
}
