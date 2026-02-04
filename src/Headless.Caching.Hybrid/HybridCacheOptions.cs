// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Caching;

/// <summary>Configuration options for the hybrid cache.</summary>
[PublicAPI]
public sealed class HybridCacheOptions : CacheOptions
{
    /// <summary>
    /// Default local (L1) cache TTL. If null, uses the same expiration as the distributed (L2) cache.
    /// </summary>
    /// <remarks>
    /// Setting a shorter TTL for L1 ensures local caches refresh more frequently from L2,
    /// reducing stale data in multi-instance deployments even if invalidation messages are missed.
    /// </remarks>
    public TimeSpan? DefaultLocalExpiration { get; set; } = TimeSpan.FromMinutes(5);

    /// <summary>
    /// Unique identifier for this cache instance. Used to filter out self-originated invalidation messages.
    /// Auto-generated if null.
    /// </summary>
    public string? InstanceId { get; set; }
}
