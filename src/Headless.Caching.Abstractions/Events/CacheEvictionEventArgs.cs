// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Caching;

/// <summary>Arguments for an in-memory eviction, carrying the reason the entry left the tier.</summary>
[PublicAPI]
public sealed class CacheEvictionEventArgs(string cacheName, CacheTier tier, string key, CacheEvictionReason reason)
    : CacheKeyEventArgs(cacheName, tier, key)
{
    /// <summary>Why the entry was evicted.</summary>
    public CacheEvictionReason Reason { get; } = reason;
}
