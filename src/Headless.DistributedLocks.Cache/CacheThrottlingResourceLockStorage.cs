using Headless.Caching;

namespace Headless.DistributedLocks.Cache;

public sealed class CacheThrottlingResourceLockStorage(ICache cache) : IThrottlingResourceLockStorage
{
    public async Task<long> GetHitCountsAsync(string resource)
    {
        var value = await cache.GetAsync<long>(resource);

        return value.HasValue ? value.Value : 0;
    }

    public async Task<long> IncrementAsync(string resource, TimeSpan ttl)
    {
        return await cache.IncrementAsync(resource, 1, ttl);
    }

    public ValueTask DisposeAsync()
    {
        return ValueTask.CompletedTask;
    }
}
