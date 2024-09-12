// Copyright (c) Mahmoud Shaheen, 2024. All rights reserved

namespace Framework.ResourceLocks.Caching;

public sealed class ScopedResourceLockStorage(
    IResourceLockStorage innerStorage,
    IResourceLockNormalizer resourceLockNormalizer
) : IResourceLockStorage
{
    public Task<bool> InsertAsync(string key, string value, TimeSpan? expiration = null)
    {
        return innerStorage.InsertAsync(resourceLockNormalizer.NormalizeResource(key), value, expiration);
    }

    public Task<bool> ReplaceIfEqualAsync<T>(string key, T value, T expected, TimeSpan? expiration = null)
    {
        return innerStorage.ReplaceIfEqualAsync(
            resourceLockNormalizer.NormalizeResource(key),
            value,
            expected,
            expiration
        );
    }

    public Task<bool> RemoveIfEqualAsync<T>(string key, T value, TimeSpan? expiration = null)
    {
        return innerStorage.RemoveIfEqualAsync(resourceLockNormalizer.NormalizeResource(key), value, expiration);
    }

    public Task<TimeSpan?> GetExpirationAsync(string key)
    {
        return innerStorage.GetExpirationAsync(resourceLockNormalizer.NormalizeResource(key));
    }

    public Task<bool> ExistsAsync(string key)
    {
        return innerStorage.ExistsAsync(resourceLockNormalizer.NormalizeResource(key));
    }
}
