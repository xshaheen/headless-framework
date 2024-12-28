// Copyright (c) Mahmoud Shaheen. All rights reserved.

#pragma warning disable IDE0130
// ReSharper disable once CheckNamespace
namespace Framework.ResourceLocks;

public interface IThrottlingResourceLockStorage : IAsyncDisposable
{
    ValueTask<long> GetHitCountsAsync(string resource, long defaultValue = 0);

    ValueTask<long> IncrementAsync(string resource, TimeSpan ttl);
}
