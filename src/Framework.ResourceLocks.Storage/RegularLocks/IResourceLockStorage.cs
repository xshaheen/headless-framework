// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Microsoft.Extensions.Options;

namespace Framework.ResourceLocks.Storage.RegularLocks;

public interface IResourceLockStorage
{
    ValueTask<bool> InsertAsync(string key, string lockId, TimeSpan? ttl = null);

    ValueTask<bool> ReplaceIfEqualAsync(string key, string lockId, string expected, TimeSpan? ttl = null);

    ValueTask<bool> RemoveAsync(string key, string lockId);

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

    public ValueTask<bool> ReplaceIfEqualAsync(string key, string lockId, string expected, TimeSpan? ttl = null)
    {
        return innerStorage.ReplaceIfEqualAsync(_NormalizeResource(key), lockId, expected, ttl);
    }

    public ValueTask<bool> RemoveAsync(string key, string lockId)
    {
        return innerStorage.RemoveAsync(_NormalizeResource(key), lockId);
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
