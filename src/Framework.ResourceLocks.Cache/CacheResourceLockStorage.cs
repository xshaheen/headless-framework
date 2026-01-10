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

    public async Task<string?> GetAsync(string key)
    {
        var result = await cache.GetAsync<string>(key);

        return result.HasValue ? result.Value : null;
    }

    public async Task<IReadOnlyDictionary<string, string>> GetAllByPrefixAsync(string prefix)
    {
        var all = await cache.GetByPrefixAsync<string>(prefix);

        return all.Where(kv => kv.Value.HasValue)
            .ToDictionary(kv => kv.Key, kv => kv.Value.Value!, StringComparer.Ordinal);
    }

    public Task<int> GetCountAsync(string prefix = "")
    {
        return cache.GetCountAsync(prefix);
    }
}
