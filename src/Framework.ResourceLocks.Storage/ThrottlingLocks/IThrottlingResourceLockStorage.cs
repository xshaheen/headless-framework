// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Framework.ResourceLocks.Storage.ThrottlingLocks;

public interface IThrottlingResourceLockStorage
{
    Task<T> GetAsync<T>(string key, T defaultValue);

    Task<long> IncrementAsync(string key, long amount, TimeSpan? expiration = null);
}

internal sealed class ScopedThrottlingResourceLockStorage(
    IThrottlingResourceLockStorage storage,
    ThrottlingResourceLockOptions options
) : IThrottlingResourceLockStorage
{
    public Task<T> GetAsync<T>(string key, T defaultValue)
    {
        return storage.GetAsync(_NormalizeResource(key), defaultValue);
    }

    public Task<long> IncrementAsync(string key, long amount, TimeSpan? expiration = null)
    {
        return storage.IncrementAsync(_NormalizeResource(key), amount, expiration);
    }

    private string _NormalizeResource(string resource) => options.KeyPrefix + resource;
}
