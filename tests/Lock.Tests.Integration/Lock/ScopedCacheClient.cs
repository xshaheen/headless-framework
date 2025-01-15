namespace Tests.Lock;

public sealed class ScopedCacheClient(ICacheClient client, string scope) : ICacheClient
{
    public Task<bool> AddAsync(string resource, string lockId, TimeSpan? ttl = null)
    {
        return client.AddAsync($"{scope}:{resource}", lockId, ttl);
    }

    public Task<T> GetAsync<T>(string cacheKey, T? defaultValue = default)
    {
        return client.GetAsync($"{scope}:{cacheKey}", defaultValue);
    }

    public Task<bool> ExistsAsync(string resource)
    {
        return client.ExistsAsync($"{scope}:{resource}");
    }

    public Task RemoveIfEqualAsync(string resource, string lockId)
    {
        return client.RemoveIfEqualAsync($"{scope}:{resource}", lockId);
    }

    public Task ReplaceIfEqualAsync(string resource, string existId, string newId, TimeSpan newTtl)
    {
        return client.ReplaceIfEqualAsync($"{scope}:{resource}", existId, newId, newTtl);
    }

    public Task<TimeSpan?> GetExpirationAsync(string resource)
    {
        return client.GetExpirationAsync($"{scope}:{resource}");
    }
}
