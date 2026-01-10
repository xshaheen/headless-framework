// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Framework.ResourceLocks.RegularLocks;

public interface IResourceLockStorage
{
    Task<bool> InsertAsync(string key, string lockId, TimeSpan? ttl = null);

    Task<bool> ReplaceIfEqualAsync(string key, string expectedId, string newId, TimeSpan? newTtl = null);

    Task<bool> RemoveIfEqualAsync(string key, string expectedId);

    Task<TimeSpan?> GetExpirationAsync(string key);

    Task<bool> ExistsAsync(string key);

    /// <summary>Gets the lock ID stored for the given key, or null if not found.</summary>
    Task<string?> GetAsync(string key);

    /// <summary>Gets all lock keys and their IDs matching the given prefix.</summary>
    Task<IReadOnlyDictionary<string, string>> GetAllByPrefixAsync(string prefix);

    /// <summary>Gets the count of locks matching the given prefix.</summary>
    Task<int> GetCountAsync(string prefix = "");
}

public sealed class ScopedResourceLockStorage(IResourceLockStorage innerStorage, string prefix) : IResourceLockStorage
{
    public Task<bool> InsertAsync(string key, string lockId, TimeSpan? ttl = null)
    {
        return innerStorage.InsertAsync(_NormalizeResource(key), lockId, ttl);
    }

    public Task<bool> ReplaceIfEqualAsync(string key, string expectedId, string newId, TimeSpan? newTtl = null)
    {
        return innerStorage.ReplaceIfEqualAsync(_NormalizeResource(key), expectedId, newId, newTtl);
    }

    public Task<bool> RemoveIfEqualAsync(string key, string expectedId)
    {
        return innerStorage.RemoveIfEqualAsync(_NormalizeResource(key), expectedId);
    }

    public Task<TimeSpan?> GetExpirationAsync(string key)
    {
        return innerStorage.GetExpirationAsync(_NormalizeResource(key));
    }

    public Task<bool> ExistsAsync(string key)
    {
        return innerStorage.ExistsAsync(_NormalizeResource(key));
    }

    public Task<string?> GetAsync(string key)
    {
        return innerStorage.GetAsync(_NormalizeResource(key));
    }

    public async Task<IReadOnlyDictionary<string, string>> GetAllByPrefixAsync(string resourcePrefix)
    {
        var result = await innerStorage.GetAllByPrefixAsync(_NormalizeResource(resourcePrefix));

        // Strip the scope prefix from keys to return unscoped resource names
        return result.ToDictionary(kv => kv.Key[prefix.Length..], kv => kv.Value, StringComparer.Ordinal);
    }

    public Task<int> GetCountAsync(string resourcePrefix = "")
    {
        return innerStorage.GetCountAsync(_NormalizeResource(resourcePrefix));
    }

    private string _NormalizeResource(string resource) => prefix + resource;
}
