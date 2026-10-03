// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Caching;

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
