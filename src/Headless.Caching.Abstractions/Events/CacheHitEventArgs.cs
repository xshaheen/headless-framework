// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Caching;

/// <summary>Arguments for a cache hit, with a flag distinguishing a fresh hit from a fail-safe stale serve.</summary>
[PublicAPI]
public sealed class CacheHitEventArgs(string cacheName, CacheTier tier, string key, bool isStale)
    : CacheKeyEventArgs(cacheName, tier, key)
{
    /// <summary>Whether the served value was a fail-safe stale reserve rather than a fresh entry.</summary>
    public bool IsStale { get; } = isStale;
}
