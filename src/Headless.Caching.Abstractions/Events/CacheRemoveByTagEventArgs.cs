// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Caching;

/// <summary>Arguments for a tag invalidation, carrying the tag. Tag invalidation is an O(1) marker bump that knows no keys.</summary>
[PublicAPI]
public sealed class CacheRemoveByTagEventArgs(string cacheName, CacheTier tier, string tag)
    : CacheEventArgs(cacheName, tier)
{
    /// <summary>The invalidation tag.</summary>
    public string Tag { get; } = tag;
}
