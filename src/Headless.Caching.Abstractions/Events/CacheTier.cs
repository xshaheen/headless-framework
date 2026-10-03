// Copyright (c) Mahmoud Shaheen. All rights reserved.

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
