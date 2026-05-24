// Copyright (c) Mahmoud Shaheen. All rights reserved.

#pragma warning disable IDE0130 // ReSharper disable once CheckNamespace
namespace Headless.DistributedLocks;

/// <summary>
/// Provides distributed reader-writer locks for resources where concurrent readers are safe and writers require exclusivity.
/// </summary>
/// <remarks>
/// Implementations may enforce writer-preference. Redis-backed locks block new readers while a writer is queued so writers
/// cannot be starved by a steady stream of readers. Returned handles use the standard <see cref="IDistributedLock"/> shape,
/// including <see cref="IDistributedLock.HandleLostToken"/> when lease monitoring is enabled.
/// </remarks>
[PublicAPI]
public interface IDistributedReaderWriterLockProvider
{
    /// <summary>
    /// Default lease duration applied when <see cref="DistributedLockAcquireOptions.TimeUntilExpires"/>
    /// is not specified. Implementations refresh the lease in storage at this cadence when
    /// <see cref="LockMonitoringMode.AutoExtend"/> is enabled.
    /// </summary>
    TimeSpan DefaultTimeUntilExpires { get; }

    /// <summary>
    /// Default upper bound applied to acquire attempts when
    /// <see cref="DistributedLockAcquireOptions.AcquireTimeout"/> is not specified. After the timeout
    /// elapses, the acquire returns <see langword="null"/> (try variants) or throws
    /// <see cref="LockAcquisitionTimeoutException"/> (acquire variants).
    /// </summary>
    TimeSpan DefaultAcquireTimeout { get; }

    /// <summary>
    /// Acquires a read lock for <paramref name="resource"/> and throws <see cref="LockAcquisitionTimeoutException"/>
    /// when it cannot be acquired before <see cref="DistributedLockAcquireOptions.AcquireTimeout"/> is reached.
    /// </summary>
    Task<IDistributedLock> AcquireReadLockAsync(
        string resource,
        DistributedLockAcquireOptions? options = null,
        CancellationToken cancellationToken = default
    );

    /// <summary>
    /// Tries to acquire a read lock for <paramref name="resource"/> and returns <see langword="null"/> on contention
    /// or timeout.
    /// </summary>
    Task<IDistributedLock?> TryAcquireReadLockAsync(
        string resource,
        DistributedLockAcquireOptions? options = null,
        CancellationToken cancellationToken = default
    );

    /// <summary>
    /// Acquires a write lock for <paramref name="resource"/> and throws <see cref="LockAcquisitionTimeoutException"/>
    /// when it cannot be acquired before <see cref="DistributedLockAcquireOptions.AcquireTimeout"/> is reached.
    /// </summary>
    Task<IDistributedLock> AcquireWriteLockAsync(
        string resource,
        DistributedLockAcquireOptions? options = null,
        CancellationToken cancellationToken = default
    );

    /// <summary>
    /// Tries to acquire a write lock for <paramref name="resource"/> and returns <see langword="null"/> on contention
    /// or timeout.
    /// </summary>
    Task<IDistributedLock?> TryAcquireWriteLockAsync(
        string resource,
        DistributedLockAcquireOptions? options = null,
        CancellationToken cancellationToken = default
    );

    /// <summary>
    /// Checks whether any reader currently holds <paramref name="resource"/>.
    /// This is an inspection primitive only; do not use it for correctness decisions.
    /// </summary>
    Task<bool> IsReadLockedAsync(string resource, CancellationToken cancellationToken = default);

    /// <summary>
    /// Checks whether a writer currently holds <paramref name="resource"/>.
    /// This is an inspection primitive only; do not use it for correctness decisions.
    /// </summary>
    Task<bool> IsWriteLockedAsync(string resource, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets the current reader count for <paramref name="resource"/>.
    /// This is an inspection primitive only; do not use it for correctness decisions.
    /// </summary>
    Task<long> GetReaderCountAsync(string resource, CancellationToken cancellationToken = default);
}
