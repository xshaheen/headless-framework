// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Caching;

/// <summary>Arguments for an eager or background refresh, carrying its kind and outcome.</summary>
[PublicAPI]
public sealed class CacheRefreshEventArgs(
    string cacheName,
    CacheTier tier,
    string key,
    CacheRefreshKind kind,
    CacheFactoryOutcome outcome
) : CacheKeyEventArgs(cacheName, tier, key)
{
    /// <summary>Whether the refresh was eager or a background completion.</summary>
    public CacheRefreshKind Kind { get; } = kind;

    /// <summary>The refresh factory's outcome.</summary>
    public CacheFactoryOutcome Outcome { get; } = outcome;
}
