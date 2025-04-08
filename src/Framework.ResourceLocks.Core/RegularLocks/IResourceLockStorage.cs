// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Framework.ResourceLocks.RegularLocks;

public interface IResourceLockStorage
{
    Task<bool> InsertAsync(string key, string lockId, TimeSpan? ttl = null);

    Task<bool> ReplaceIfEqualAsync(string key, string expectedId, string newId, TimeSpan? newTtl = null);

    Task<bool> RemoveIfEqualAsync(string key, string expectedId);

    Task<TimeSpan?> GetExpirationAsync(string key);

    Task<bool> ExistsAsync(string key);
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

    private string _NormalizeResource(string resource) => prefix + resource;
}
