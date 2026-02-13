using Headless.Caching;

namespace Headless.DistributedLocks.Cache;

public sealed class CacheDistributedLockStorage(ICache cache) : IDistributedLockStorage
{
    public ValueTask<bool> InsertAsync(string key, string lockId, TimeSpan? ttl = null) =>
        cache.TryInsertAsync(key, lockId, ttl);

    public ValueTask<bool> ReplaceIfEqualAsync(string key, string expectedId, string newId, TimeSpan? newTtl = null) =>
        cache.TryReplaceIfEqualAsync(key, expectedId, newId, newTtl);

    public ValueTask<bool> RemoveIfEqualAsync(string key, string expectedId) =>
        cache.RemoveIfEqualAsync(key, expectedId);

    public ValueTask<TimeSpan?> GetExpirationAsync(string key) =>
        cache.GetExpirationAsync(key);

    public ValueTask<bool> ExistsAsync(string key) =>
        cache.ExistsAsync(key);

    public async ValueTask<string?> GetAsync(string key)
    {
        var result = await cache.GetAsync<string>(key);

        return result.HasValue ? result.Value : null;
    }

    public async ValueTask<IReadOnlyDictionary<string, string>> GetAllByPrefixAsync(string prefix)
    {
        var all = await cache.GetByPrefixAsync<string>(prefix);

        return all.Where(kv => kv.Value.HasValue)
            .ToDictionary(kv => kv.Key, kv => kv.Value.Value!, StringComparer.Ordinal);
    }

    public ValueTask<long> GetCountAsync(string prefix = "") =>
        cache.GetCountAsync(prefix);
}
