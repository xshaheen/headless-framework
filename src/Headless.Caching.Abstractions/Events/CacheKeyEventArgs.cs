// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Caching;

/// <summary>Base arguments for events that concern a single cache entry, carrying its caller-facing key.</summary>
[PublicAPI]
public class CacheKeyEventArgs(string cacheName, CacheTier tier, string key) : CacheEventArgs(cacheName, tier)
{
    /// <summary>The caller-facing cache key the event concerns (never the internally-prefixed store key).</summary>
    public string Key { get; } = key;
}
