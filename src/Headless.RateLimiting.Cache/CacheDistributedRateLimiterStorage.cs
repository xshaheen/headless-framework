// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Caching;
using Headless.RateLimiting;

namespace Headless.RateLimiting.Cache;

[PublicAPI]
public sealed class CacheDistributedRateLimiterStorage(ICache cache) : IDistributedRateLimiterStorage
{
    public async Task<long> GetHitCountsAsync(string resource, CancellationToken cancellationToken = default)
    {
        var value = await cache.GetAsync<long>(resource, cancellationToken);

        return value.HasValue ? value.Value : 0;
    }

    public async Task<long> IncrementAsync(string resource, TimeSpan ttl, CancellationToken cancellationToken = default)
    {
        return await cache.IncrementAsync(resource, 1L, ttl, cancellationToken);
    }

    public ValueTask DisposeAsync()
    {
        return ValueTask.CompletedTask;
    }
}
