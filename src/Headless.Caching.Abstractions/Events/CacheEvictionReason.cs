// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Caching;

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
