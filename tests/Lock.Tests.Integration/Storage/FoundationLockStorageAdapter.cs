using Foundatio.Caching;
using ICacheClient = Tests.Lock.ICacheClient;
using IFoundatioCacheClient = Foundatio.Caching.ICacheClient;

namespace Tests.Storage;

public sealed class FoundationLockStorageAdapter(IFoundatioCacheClient cacheClient) : ICacheClient, IDisposable
{
    public Task<bool> AddAsync(string resource, string lockId, TimeSpan? ttl = null)
    {
        return cacheClient.AddAsync(resource, lockId, ttl);
    }

    public Task<T> GetAsync<T>(string cacheKey, T? defaultValue = default)
    {
        return cacheClient.GetAsync<T>(cacheKey, defaultValue);
    }

    public Task<bool> ExistsAsync(string resource)
    {
        return cacheClient.ExistsAsync(resource);
    }

    public Task RemoveIfEqualAsync(string resource, string lockId)
    {
        return cacheClient.RemoveIfEqualAsync(resource, lockId);
    }

    public Task ReplaceIfEqualAsync(string resource, string existId, string newId, TimeSpan newTtl)
    {
        return cacheClient.ReplaceIfEqualAsync(resource, newId, existId, newTtl);
    }

    public Task<TimeSpan?> GetExpirationAsync(string resource)
    {
        return cacheClient.GetExpirationAsync(resource);
    }

    public void Dispose()
    {
        cacheClient.Dispose();
    }
}
