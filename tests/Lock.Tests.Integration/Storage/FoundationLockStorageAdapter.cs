using Framework.ResourceLocks.RegularLocks;
using IFoundatioCacheClient = Foundatio.Caching.ICacheClient;

namespace Tests.Storage;

public sealed class FoundationLockStorageAdapter(IFoundatioCacheClient cacheClient) : IResourceLockStorage
{
    public Task<bool> InsertAsync(string key, string lockId, TimeSpan? ttl = null)
    {
        return cacheClient.AddAsync(key, lockId, ttl);
    }

    public Task<bool> ReplaceIfEqualAsync(string key, string expectedId, string newId, TimeSpan? newTtl = null)
    {
        return cacheClient.ReplaceIfEqualAsync(key, expectedId, newId, newTtl);
    }

    public Task<bool> RemoveIfEqualAsync(string key, string expectedId)
    {
        return cacheClient.RemoveIfEqualAsync(key, expectedId);
    }

    public Task<TimeSpan?> GetExpirationAsync(string key)
    {
        return cacheClient.GetExpirationAsync(key);
    }

    public Task<bool> ExistsAsync(string key)
    {
        return cacheClient.ExistsAsync(key);
    }
}
