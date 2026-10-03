// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Caching;

/// <summary>Arguments for a bulk <c>RemoveAllAsync</c>, carrying the number of entries removed.</summary>
[PublicAPI]
public sealed class CacheRemoveAllEventArgs(string cacheName, CacheTier tier, int removedCount)
    : CacheEventArgs(cacheName, tier)
{
    /// <summary>The number of entries removed.</summary>
    public int RemovedCount { get; } = removedCount;
}
