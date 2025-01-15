using Foundatio.Caching;
using Framework.ResourceLocks;

namespace Tests.Storage;

public sealed class FoundationThrottlingStorageAdapter(ICacheClient cacheClient) : IThrottlingResourceLockStorage
{
    public async Task<long> GetHitCountsAsync(string resource)
    {
        return await cacheClient.GetAsync(resource, 0);
    }

    public async Task<long> IncrementAsync(string resource, TimeSpan ttl)
    {
        return await cacheClient.IncrementAsync(resource, 1, ttl);
    }

    public ValueTask DisposeAsync()
    {
        return ValueTask.CompletedTask;
    }
}
