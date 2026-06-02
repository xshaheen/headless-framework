// Copyright (c) Mahmoud Shaheen. All rights reserved.

#pragma warning disable IDE0130 // ReSharper disable once CheckNamespace
namespace Headless.DistributedLocks;

[PublicAPI]
public interface IDistributedSemaphoreStorage
{
    ValueTask<DistributedLockAcquireResult> TryAcquireAsync(
        string resource,
        string lockId,
        int maxCount,
        TimeSpan ttl,
        CancellationToken cancellationToken = default
    );

    ValueTask<bool> TryExtendAsync(
        string resource,
        string lockId,
        TimeSpan ttl,
        CancellationToken cancellationToken = default
    );

    ValueTask<bool> ValidateAsync(string resource, string lockId, CancellationToken cancellationToken = default);

    ValueTask<bool> ReleaseAsync(string resource, string lockId, CancellationToken cancellationToken = default);

    ValueTask<long> GetCountAsync(string resource, CancellationToken cancellationToken = default);
}
