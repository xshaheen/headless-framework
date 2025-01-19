using Framework.Caching;
using Framework.ResourceLocks.RegularLocks;

namespace Framework.ResourceLocks.Cache;

public sealed class CacheResourceLockStorage(ICache cache) : IResourceLockStorage
{
    public Task<bool> InsertAsync(string key, string lockId, TimeSpan? ttl = null)
    {
        return cache.TryInsertAsync(key, lockId, ttl);
    }

    public Task<bool> ReplaceIfEqualAsync(string key, string expectedId, string newId, TimeSpan? newTtl = null)
    {
        return cache.TryReplaceIfEqualAsync(key, expectedId, newId, newTtl);
    }

    public Task<bool> RemoveIfEqualAsync(string key, string expectedId)
    {
        return cache.RemoveIfEqualAsync(key, expectedId);
    }

    public Task<TimeSpan?> GetExpirationAsync(string key)
    {
        return cache.GetExpirationAsync(key);
    }

    public Task<bool> ExistsAsync(string key)
    {
        return cache.ExistsAsync(key);
    }
}
