// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Caching;

/// <summary>Arguments for a fail-safe stale-serving activation.</summary>
[PublicAPI]
public sealed class CacheFailSafeEventArgs(string cacheName, CacheTier tier, string key, CacheFailSafeTrigger trigger)
    : CacheKeyEventArgs(cacheName, tier, key)
{
    /// <summary>What triggered the activation.</summary>
    public CacheFailSafeTrigger Trigger { get; } = trigger;
}
