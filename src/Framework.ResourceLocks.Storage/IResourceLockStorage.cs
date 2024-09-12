// Copyright (c) Mahmoud Shaheen, 2024. All rights reserved

namespace Framework.ResourceLocks.Caching;

public interface IResourceLockStorage
{
    Task<bool> InsertAsync(string key, string value, TimeSpan? expiration = null);

    Task<bool> ReplaceIfEqualAsync<T>(string key, T value, T expected, TimeSpan? expiration = null);

    Task<bool> RemoveIfEqualAsync<T>(string key, T value, TimeSpan? expiration = null);

    Task<TimeSpan?> GetExpirationAsync(string key);

    Task<bool> ExistsAsync(string key);
}
