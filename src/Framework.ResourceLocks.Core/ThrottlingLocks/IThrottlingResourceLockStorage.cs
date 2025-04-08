// Copyright (c) Mahmoud Shaheen. All rights reserved.

#pragma warning disable IDE0130
// ReSharper disable once CheckNamespace
namespace Framework.ResourceLocks;

public interface IThrottlingResourceLockStorage : IAsyncDisposable
{
    Task<long> GetHitCountsAsync(string resource);

    Task<long> IncrementAsync(string resource, TimeSpan ttl);
}
