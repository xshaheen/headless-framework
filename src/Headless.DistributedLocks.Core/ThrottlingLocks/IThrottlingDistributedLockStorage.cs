// Copyright (c) Mahmoud Shaheen. All rights reserved.

#pragma warning disable IDE0130
// ReSharper disable once CheckNamespace
namespace Headless.DistributedLocks;

public interface IThrottlingDistributedLockStorage : IAsyncDisposable
{
    Task<long> GetHitCountsAsync(string resource, CancellationToken cancellationToken = default);

    Task<long> IncrementAsync(string resource, TimeSpan ttl, CancellationToken cancellationToken = default);
}
