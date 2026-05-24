// Copyright (c) Mahmoud Shaheen. All rights reserved.

#pragma warning disable IDE0130 // ReSharper disable once CheckNamespace
namespace Headless.DistributedLocks;

/// <summary>Storage contract for atomic distributed reader-writer lock operations.</summary>
public interface IDistributedReaderWriterLockStorage
{
    ValueTask<bool> TryAcquireReadAsync(
        string resource,
        string lockId,
        TimeSpan? ttl = null,
        CancellationToken cancellationToken = default
    );

    ValueTask<bool> TryExtendReadAsync(
        string resource,
        string lockId,
        TimeSpan? ttl = null,
        CancellationToken cancellationToken = default
    );

    ValueTask ReleaseReadAsync(string resource, string lockId, CancellationToken cancellationToken = default);

    ValueTask<bool> TryAcquireWriteAsync(
        string resource,
        string lockId,
        string waitingId,
        TimeSpan? ttl = null,
        CancellationToken cancellationToken = default
    );

    ValueTask<bool> TryExtendWriteAsync(
        string resource,
        string lockId,
        TimeSpan? ttl = null,
        CancellationToken cancellationToken = default
    );

    ValueTask ReleaseWriteAsync(string resource, string lockId, CancellationToken cancellationToken = default);

    ValueTask<bool> ValidateReadAsync(string resource, string lockId, CancellationToken cancellationToken = default);

    ValueTask<bool> ValidateWriteAsync(string resource, string lockId, CancellationToken cancellationToken = default);

    ValueTask<bool> IsReadLockedAsync(string resource, CancellationToken cancellationToken = default);

    ValueTask<bool> IsWriteLockedAsync(string resource, CancellationToken cancellationToken = default);

    ValueTask<long> GetReaderCountAsync(string resource, CancellationToken cancellationToken = default);
}
