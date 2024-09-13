// Copyright (c) Mahmoud Shaheen, 2024. All rights reserved

using Microsoft.Extensions.Options;

namespace Framework.ResourceLocks.Storage.RegularLocks;

public interface IResourceLockStorage
{
    Task<bool> InsertAsync(string key, string value, TimeSpan? expiration = null);

    Task<bool> ReplaceIfEqualAsync<T>(string key, T value, T expected, TimeSpan? expiration = null);

    Task<bool> RemoveIfEqualAsync<T>(string key, T value, TimeSpan? expiration = null);

    Task<TimeSpan?> GetExpirationAsync(string key);

    Task<bool> ExistsAsync(string key);
}

internal sealed class ScopedResourceLockStorage(
    IResourceLockStorage innerStorage,
    IOptions<ResourceLockOptions> optionsAccessor
) : IResourceLockStorage
{
    private readonly ResourceLockOptions _options = optionsAccessor.Value;

    public Task<bool> InsertAsync(string key, string value, TimeSpan? expiration = null)
    {
        return innerStorage.InsertAsync(_NormalizeResource(key), value, expiration);
    }

    public Task<bool> ReplaceIfEqualAsync<T>(string key, T value, T expected, TimeSpan? expiration = null)
    {
        return innerStorage.ReplaceIfEqualAsync(_NormalizeResource(key), value, expected, expiration);
    }

    public Task<bool> RemoveIfEqualAsync<T>(string key, T value, TimeSpan? expiration = null)
    {
        return innerStorage.RemoveIfEqualAsync(_NormalizeResource(key), value, expiration);
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
