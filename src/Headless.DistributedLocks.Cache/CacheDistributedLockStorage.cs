using Headless.Caching;

namespace Headless.DistributedLocks.Cache;

public sealed class CacheDistributedLockStorage(ICache cache) : IDistributedLockStorage
{
    public ValueTask<bool> InsertAsync(
        string key,
        string lockId,
        TimeSpan? ttl = null,
        CancellationToken cancellationToken = default
    )
    {
        return cache.TryInsertAsync(key, lockId, ttl, cancellationToken);
    }

    public ValueTask<bool> ReplaceIfEqualAsync(
        string key,
        string expectedId,
        string newId,
        TimeSpan? newTtl = null,
        CancellationToken cancellationToken = default
    )
    {
        return cache.TryReplaceIfEqualAsync(key, expectedId, newId, newTtl, cancellationToken);
    }

    public ValueTask<bool> RemoveIfEqualAsync(
        string key,
        string expectedId,
        CancellationToken cancellationToken = default
    )
    {
        return cache.RemoveIfEqualAsync(key, expectedId, cancellationToken);
    }

    public ValueTask<TimeSpan?> GetExpirationAsync(string key, CancellationToken cancellationToken = default)
    {
        return cache.GetExpirationAsync(key, cancellationToken);
    }

    public ValueTask<bool> ExistsAsync(string key, CancellationToken cancellationToken = default)
    {
        return cache.ExistsAsync(key, cancellationToken);
    }

    public async ValueTask<string?> GetAsync(string key, CancellationToken cancellationToken = default)
    {
        var result = await cache.GetAsync<string>(key, cancellationToken).ConfigureAwait(false);

        return result.HasValue ? result.Value : null;
    }

    public async ValueTask<IReadOnlyDictionary<string, string>> GetAllByPrefixAsync(
        string prefix,
        CancellationToken cancellationToken = default
    )
    {
        var all = await cache.GetByPrefixAsync<string>(prefix, cancellationToken).ConfigureAwait(false);

        return all.Where(kv => kv.Value.HasValue)
            .ToDictionary(kv => kv.Key, kv => kv.Value.Value!, StringComparer.Ordinal);
    }

    public async ValueTask<IReadOnlyDictionary<string, (string LockId, TimeSpan? Ttl)>> GetAllWithExpirationByPrefixAsync(
        string prefix,
        CancellationToken cancellationToken = default
    )
    {
        var all = await cache.GetByPrefixAsync<string>(prefix, cancellationToken).ConfigureAwait(false);
        var result = new Dictionary<string, (string, TimeSpan?)>(all.Count, StringComparer.Ordinal);

        foreach (var (key, value) in all)
        {
            if (value.HasValue)
            {
                var ttl = await cache.GetExpirationAsync(key, cancellationToken).ConfigureAwait(false);
                result[key] = (value.Value!, ttl);
            }
        }

        return result;
    }

    public ValueTask<long> GetCountAsync(string prefix = "", CancellationToken cancellationToken = default)
    {
        return cache.GetCountAsync(prefix, cancellationToken);
    }
}
