using Headless.Caching;

namespace Headless.DistributedLocks.Cache;

public sealed class CacheThrottlingDistributedLockStorage(ICache cache) : IThrottlingDistributedLockStorage
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
