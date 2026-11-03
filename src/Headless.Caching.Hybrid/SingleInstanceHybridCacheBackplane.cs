// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Caching;

/// <summary>
/// The backplane for a host that is the only one running its hybrid caches: there is no peer whose L1 a write could
/// make stale, so an invalidation has nowhere to go and is dropped. Selected explicitly with
/// <c>UseSingleInstanceBackplane()</c>, never by default, because on a second host it would leave both serving stale L1
/// entries.
/// </summary>
internal sealed class SingleInstanceHybridCacheBackplane : IHybridCacheBackplane
{
    public ValueTask PublishAsync(CacheInvalidationMessage message, CancellationToken cancellationToken)
    {
        return ValueTask.CompletedTask;
    }
}
