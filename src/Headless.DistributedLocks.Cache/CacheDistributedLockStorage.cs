using Headless.Caching;

namespace Headless.DistributedLocks.Cache;

public sealed class CacheDistributedLockStorage(ICache cache) : IDistributedLockStorage
{
    public ValueTask<bool> InsertAsync(string key, string lockId, TimeSpan? ttl = null, CancellationToken cancellationToken = default) =>
        cache.TryInsertAsync(key, lockId, ttl, cancellationToken);

    public ValueTask<bool> ReplaceIfEqualAsync(string key, string expectedId, string newId, TimeSpan? newTtl = null, CancellationToken cancellationToken = default) =>
        cache.TryReplaceIfEqualAsync(key, expectedId, newId, newTtl, cancellationToken);

    public ValueTask<bool> RemoveIfEqualAsync(string key, string expectedId, CancellationToken cancellationToken = default) =>
        cache.RemoveIfEqualAsync(key, expectedId, cancellationToken);

    public ValueTask<TimeSpan?> GetExpirationAsync(string key, CancellationToken cancellationToken = default) =>
        cache.GetExpirationAsync(key, cancellationToken);

    public ValueTask<bool> ExistsAsync(string key, CancellationToken cancellationToken = default) =>
        cache.ExistsAsync(key, cancellationToken);

    public async ValueTask<string?> GetAsync(string key, CancellationToken cancellationToken = default)
    {
        var result = await cache.GetAsync<string>(key, cancellationToken);

        return result.HasValue ? result.Value : null;
    }

    public async ValueTask<IReadOnlyDictionary<string, string>> GetAllByPrefixAsync(string prefix, CancellationToken cancellationToken = default)
    {
        var all = await cache.GetByPrefixAsync<string>(prefix, cancellationToken);

        return all.Where(kv => kv.Value.HasValue)
            .ToDictionary(kv => kv.Key, kv => kv.Value.Value!, StringComparer.Ordinal);
    }

    public ValueTask<long> GetCountAsync(string prefix = "", CancellationToken cancellationToken = default) =>
        cache.GetCountAsync(prefix, cancellationToken);
}
