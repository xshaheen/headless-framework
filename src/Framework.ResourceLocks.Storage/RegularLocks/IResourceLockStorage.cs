// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Microsoft.Extensions.Options;

namespace Framework.ResourceLocks.Storage.RegularLocks;

public interface IResourceLockStorage
{
    ValueTask<bool> InsertAsync(string key, string lockId, TimeSpan? ttl = null);

    ValueTask<bool> ReplaceIfHasIdAsync(string key, string expectedId, string newId, TimeSpan? newTtl = null);

    ValueTask<bool> RemoveIfHasIdAsync(string key, string expectedId);

    ValueTask<TimeSpan?> GetExpirationAsync(string key);

    ValueTask<bool> ExistsAsync(string key);
}

internal sealed class ScopedResourceLockStorage(
    IResourceLockStorage innerStorage,
    IOptions<ResourceLockOptions> optionsAccessor
) : IResourceLockStorage
{
    private readonly ResourceLockOptions _options = optionsAccessor.Value;

    public ValueTask<bool> InsertAsync(string key, string lockId, TimeSpan? ttl = null)
    {
        return innerStorage.InsertAsync(_NormalizeResource(key), lockId, ttl);
    }

    public ValueTask<bool> ReplaceIfHasIdAsync(string key, string expectedId, string newId, TimeSpan? newTtl = null)
    {
        return innerStorage.ReplaceIfHasIdAsync(_NormalizeResource(key), expectedId, newId, newTtl);
    }

    public ValueTask<bool> RemoveIfHasIdAsync(string key, string expectedId)
    {
        return innerStorage.RemoveIfHasIdAsync(_NormalizeResource(key), expectedId);
    }

    public ValueTask<TimeSpan?> GetExpirationAsync(string key)
    {
        return innerStorage.GetExpirationAsync(_NormalizeResource(key));
    }

    public ValueTask<bool> ExistsAsync(string key)
    {
        return innerStorage.ExistsAsync(_NormalizeResource(key));
    }

    private string _NormalizeResource(string resource) => _options.KeyPrefix + resource;
}
