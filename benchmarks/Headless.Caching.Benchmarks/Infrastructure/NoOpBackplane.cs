// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Caching.Benchmarks.Infrastructure;

/// <summary>Drops every invalidation: the benchmark runs one hybrid, so there is no peer L1 to evict.</summary>
internal sealed class NoOpBackplane : IHybridCacheBackplane
{
    public ValueTask PublishAsync(CacheInvalidationMessage message, CancellationToken cancellationToken) =>
        ValueTask.CompletedTask;
}
