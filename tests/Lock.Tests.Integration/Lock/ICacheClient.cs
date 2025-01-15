namespace Tests.Lock;

public interface ICacheClient
{
    Task<bool> AddAsync(string resource, string lockId, TimeSpan? ttl = null);

    Task<T> GetAsync<T>(string cacheKey, T? defaultValue = default);

    Task<bool> ExistsAsync(string resource);

    Task RemoveIfEqualAsync(string resource, string lockId);

    Task ReplaceIfEqualAsync(string resource, string existId, string newId, TimeSpan newTtl);

    Task<TimeSpan?> GetExpirationAsync(string resource);
}
