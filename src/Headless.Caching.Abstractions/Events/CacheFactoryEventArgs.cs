// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Caching;

/// <summary>Arguments for a factory execution outcome (success, error, or timeout).</summary>
[PublicAPI]
public sealed class CacheFactoryEventArgs(string cacheName, CacheTier tier, string key, CacheFactoryOutcome outcome)
    : CacheKeyEventArgs(cacheName, tier, key)
{
    /// <summary>The factory's outcome.</summary>
    public CacheFactoryOutcome Outcome { get; } = outcome;
}
