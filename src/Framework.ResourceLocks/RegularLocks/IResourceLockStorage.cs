// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Microsoft.Extensions.Options;

namespace Framework.ResourceLocks.RegularLocks;

public interface IResourceLockStorage
{
    Task<bool> InsertAsync(string key, string lockId, TimeSpan? ttl = null);

    Task<bool> ReplaceIfHasIdAsync(string key, string expectedId, string newId, TimeSpan? newTtl = null);

    Task<bool> RemoveIfHasIdAsync(string key, string expectedId);

    Task<TimeSpan?> GetExpirationAsync(string key);

    Task<bool> ExistsAsync(string key);
}

internal sealed class ScopedResourceLockStorage(
    IResourceLockStorage innerStorage,
    IOptions<ResourceLockOptions> optionsAccessor
) : IResourceLockStorage
{
    private readonly ResourceLockOptions _options = optionsAccessor.Value;

    public Task<bool> InsertAsync(string key, string lockId, TimeSpan? ttl = null)
    {
        return innerStorage.InsertAsync(_NormalizeResource(key), lockId, ttl);
    }

    public Task<bool> ReplaceIfHasIdAsync(string key, string expectedId, string newId, TimeSpan? newTtl = null)
    {
        return innerStorage.ReplaceIfHasIdAsync(_NormalizeResource(key), expectedId, newId, newTtl);
    }

    public Task<bool> RemoveIfHasIdAsync(string key, string expectedId)
    {
        return innerStorage.RemoveIfHasIdAsync(_NormalizeResource(key), expectedId);
    }

    public Task<TimeSpan?> GetExpirationAsync(string key)
    {
        return innerStorage.GetExpirationAsync(_NormalizeResource(key));
    }

    public Task<bool> ExistsAsync(string key)
    {
        return innerStorage.ExistsAsync(_NormalizeResource(key));
    }

    private string _NormalizeResource(string resource) => _options.KeyPrefix + resource;
}
