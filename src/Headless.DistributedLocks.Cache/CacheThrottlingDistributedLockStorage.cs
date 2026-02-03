using Headless.Caching;

namespace Headless.DistributedLocks.Cache;

public sealed class CacheThrottlingDistributedLockStorage(ICache cache) : IThrottlingDistributedLockStorage
{
    public async Task<long> GetHitCountsAsync(string resource)
    {
        var value = await cache.GetAsync<long>(resource);

        return value.HasValue ? value.Value : 0;
    }

    public async Task<long> IncrementAsync(string resource, TimeSpan ttl)
    {
        return await cache.IncrementAsync(resource, 1L, ttl);
    }

    public ValueTask DisposeAsync()
    {
        return ValueTask.CompletedTask;
    }
}
