// Copyright (c) Mahmoud Shaheen. All rights reserved.

#pragma warning disable IDE0130 // ReSharper disable once CheckNamespace
namespace Headless.DistributedLocks;

/// <summary>
/// An N-holder distributed semaphore for coordinating up to <see cref="MaxCount"/> concurrent
/// holders of one resource.
/// </summary>
[PublicAPI]
public interface IDistributedSemaphore
{
    /// <summary>The resource guarded by this semaphore.</summary>
    string Resource { get; }

    /// <summary>The maximum number of concurrent holders allowed for <see cref="Resource"/>.</summary>
    int MaxCount { get; }

    /// <summary>
    /// Acquires a semaphore slot and throws <see cref="LockAcquisitionTimeoutException"/> when
    /// no slot becomes available before the acquire timeout.
    /// </summary>
    Task<IDistributedLock> AcquireAsync(
        DistributedLockAcquireOptions? options = null,
        CancellationToken cancellationToken = default
    );

    /// <summary>
    /// Attempts to acquire a semaphore slot, returning null when no slot becomes available before
    /// the acquire timeout.
    /// </summary>
    Task<IDistributedLock?> TryAcquireAsync(
        DistributedLockAcquireOptions? options = null,
        CancellationToken cancellationToken = default
    );
}
