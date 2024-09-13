// Copyright (c) Mahmoud Shaheen, 2024. All rights reserved

using Microsoft.Extensions.Options;

namespace Framework.ResourceLocks.Storage.ThrottlingLocks;

public interface IThrottlingResourceLockStorage
{
    Task<T> GetAsync<T>(string key, T defaultValue);

    Task<long> IncrementAsync(string key, long amount, TimeSpan? expiration = null);
}

internal sealed class ScopedThrottlingResourceLockStorage(
    IThrottlingResourceLockStorage storage,
    IOptions<ThrottlingResourceLockOptions> optionsAccessor
) : IThrottlingResourceLockStorage
{
    private readonly ThrottlingResourceLockOptions _options = optionsAccessor.Value;

    public Task<T> GetAsync<T>(string key, T defaultValue)
    {
        return storage.GetAsync(_NormalizeResource(key), defaultValue);
    }

    public Task<long> IncrementAsync(string key, long amount, TimeSpan? expiration = null)
    {
        return storage.IncrementAsync(_NormalizeResource(key), amount, expiration);
    }

    private string _NormalizeResource(string resource) => _options.KeyPrefix + resource;
}
