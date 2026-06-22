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
    /// <param name="options">Per-call configuration. <see langword="null"/> applies the defaults. See <see cref="DistributedLockAcquireOptions"/>.</param>
    /// <param name="cancellationToken">Cancels the acquire attempt; surfaces as <see cref="OperationCanceledException"/>.</param>
    /// <returns>The acquired slot lease. Never <see langword="null"/> — throws on timeout instead.</returns>
    /// <exception cref="ArgumentException">
    /// <see cref="DistributedLockAcquireOptions.TimeUntilExpires"/> is <see cref="Timeout.InfiniteTimeSpan"/>
    /// (a slot is stored with a finite expiry score, so a non-expiring lease is rejected regardless of
    /// <see cref="DistributedLockAcquireOptions.Monitoring"/>).
    /// </exception>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <see cref="DistributedLockAcquireOptions.AcquireTimeout"/> /
    /// <see cref="DistributedLockAcquireOptions.TimeUntilExpires"/> is negative or too large.
    /// </exception>
    /// <exception cref="LockAcquisitionTimeoutException">No slot became available before the acquire timeout elapsed.</exception>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was cancelled.</exception>
    Task<IDistributedLease> AcquireAsync(
        DistributedLockAcquireOptions? options = null,
        CancellationToken cancellationToken = default
    );

    /// <summary>
    /// Attempts to acquire a semaphore slot, returning null when no slot becomes available before
    /// the acquire timeout.
    /// </summary>
    /// <param name="options">Per-call configuration. <see langword="null"/> applies the defaults. See <see cref="DistributedLockAcquireOptions"/>.</param>
    /// <param name="cancellationToken">Cancels the acquire attempt; surfaces as <see cref="OperationCanceledException"/>.</param>
    /// <returns>The acquired slot lease, or <see langword="null"/> if no slot became available before the acquire timeout.</returns>
    /// <exception cref="ArgumentException">
    /// <see cref="DistributedLockAcquireOptions.TimeUntilExpires"/> is <see cref="Timeout.InfiniteTimeSpan"/>
    /// (a slot is stored with a finite expiry score, so a non-expiring lease is rejected).
    /// </exception>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <see cref="DistributedLockAcquireOptions.AcquireTimeout"/> /
    /// <see cref="DistributedLockAcquireOptions.TimeUntilExpires"/> is negative or too large.
    /// </exception>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was cancelled.</exception>
    Task<IDistributedLease?> TryAcquireAsync(
        DistributedLockAcquireOptions? options = null,
        CancellationToken cancellationToken = default
    );
}
