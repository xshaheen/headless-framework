// Copyright (c) Mahmoud Shaheen. All rights reserved.

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
