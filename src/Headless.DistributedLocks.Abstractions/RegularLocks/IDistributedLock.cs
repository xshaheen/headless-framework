// Copyright (c) Mahmoud Shaheen. All rights reserved.

#pragma warning disable IDE0130 // ReSharper disable once CheckNamespace
namespace Headless.DistributedLocks;

/// <summary>Provides methods to acquire, release, and manage resource locks.</summary>
[PublicAPI]
public interface IDistributedLock
{
    /// <summary>
    /// Default lease duration applied when <see cref="DistributedLockAcquireOptions.TimeUntilExpires"/> is not
    /// specified on an acquire call. Implementations refresh the lease in storage at this cadence when
    /// <see cref="LockMonitoringMode.AutoExtend"/> is enabled.
    /// </summary>
    TimeSpan DefaultTimeUntilExpires { get; }

    /// <summary>
    /// Default upper bound applied to acquire attempts when
    /// <see cref="DistributedLockAcquireOptions.AcquireTimeout"/> is not specified. After it elapses,
    /// <see cref="AcquireAsync"/> throws <see cref="LockAcquisitionTimeoutException"/> and
    /// <see cref="TryAcquireAsync"/> returns <see langword="null"/>.
    /// </summary>
    TimeSpan DefaultAcquireTimeout { get; }

    /// <summary>
    /// Acquires a resource lock for a specified resource and throws
    /// <see cref="LockAcquisitionTimeoutException"/> if the lock is not acquired before
    /// <see cref="DistributedLockAcquireOptions.AcquireTimeout"/> is reached.
    /// </summary>
    /// <param name="resource">The resource to acquire the lock for. Must be non-null and non-whitespace.</param>
    /// <param name="options">
    /// Per-call configuration (lease TTL, acquire timeout, release-on-dispose, monitoring mode).
    /// <see langword="null"/> applies the lock defaults. See <see cref="DistributedLockAcquireOptions"/>.
    /// </param>
    /// <param name="cancellationToken">
    /// Cancels the acquire attempt and any wait/retry loop. Cancellation surfaces as
    /// <see cref="OperationCanceledException"/>.
    /// </param>
    /// <returns>
    /// The acquired lease. This method never returns <see langword="null"/> — it throws
    /// <see cref="LockAcquisitionTimeoutException"/> on timeout instead.
    /// </returns>
    /// <exception cref="ArgumentNullException"><paramref name="resource"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">
    /// <paramref name="resource"/> is empty or whitespace; or <see cref="DistributedLockAcquireOptions.Monitoring"/>
    /// is <see cref="LockMonitoringMode.Monitor"/> or <see cref="LockMonitoringMode.AutoExtend"/> but
    /// <see cref="DistributedLockAcquireOptions.TimeUntilExpires"/> is <see cref="Timeout.InfiniteTimeSpan"/>
    /// (monitoring requires a finite lease).
    /// </exception>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="resource"/> exceeds the provider's maximum resource-name length, or
    /// <see cref="DistributedLockAcquireOptions.AcquireTimeout"/> /
    /// <see cref="DistributedLockAcquireOptions.TimeUntilExpires"/> is negative (other than
    /// <see cref="Timeout.InfiniteTimeSpan"/>) or too large.
    /// </exception>
    /// <exception cref="LockAcquisitionTimeoutException">
    /// The lock could not be acquired before <see cref="DistributedLockAcquireOptions.AcquireTimeout"/> elapsed.
    /// </exception>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was cancelled.</exception>
    Task<IDistributedLease> AcquireAsync(
        string resource,
        DistributedLockAcquireOptions? options = null,
        CancellationToken cancellationToken = default
    );

    /// <summary>
    /// Acquires a resource lock for a specified resource this method will block
    /// until the lock is acquired or the <see cref="DistributedLockAcquireOptions.AcquireTimeout"/> is reached.
    /// </summary>
    /// <param name="resource">The resource to acquire the lock for. Must be non-null and non-whitespace.</param>
    /// <param name="options">
    /// Per-call configuration (lease TTL, acquire timeout, release-on-dispose, monitoring mode).
    /// <see langword="null"/> applies the lock defaults. See <see cref="DistributedLockAcquireOptions"/>.
    /// </param>
    /// <param name="cancellationToken">
    /// Cancels the acquire attempt and any wait/retry loop. Cancellation surfaces as
    /// <see cref="OperationCanceledException"/>.
    /// </param>
    /// <returns>
    /// A task that represents the asynchronous operation. The task result contains the acquired lease,
    /// or <see langword="null"/> if the lock could not be acquired before the acquire timeout.
    /// </returns>
    /// <exception cref="ArgumentNullException"><paramref name="resource"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">
    /// <paramref name="resource"/> is empty or whitespace; or <see cref="DistributedLockAcquireOptions.Monitoring"/>
    /// is <see cref="LockMonitoringMode.Monitor"/> or <see cref="LockMonitoringMode.AutoExtend"/> but
    /// <see cref="DistributedLockAcquireOptions.TimeUntilExpires"/> is <see cref="Timeout.InfiniteTimeSpan"/>
    /// (monitoring requires a finite lease).
    /// </exception>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="resource"/> exceeds the provider's maximum resource-name length, or
    /// <see cref="DistributedLockAcquireOptions.AcquireTimeout"/> /
    /// <see cref="DistributedLockAcquireOptions.TimeUntilExpires"/> is negative (other than
    /// <see cref="Timeout.InfiniteTimeSpan"/>) or too large.
    /// </exception>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was cancelled.</exception>
    /// <remarks>
    /// When <see cref="DistributedLockAcquireOptions.AcquireTimeout"/> is <see cref="TimeSpan.Zero"/> the
    /// implementation runs a single storage attempt with no retry loop (the "try once, no wait" semantic).
    /// The attempt is bounded by an internal safety deadline so a stalled lock-store call cannot hang the
    /// caller indefinitely, even when <paramref name="cancellationToken"/> is <see cref="CancellationToken.None"/>.
    /// The deadline is a ceiling on the storage round-trip, not a wait budget — under healthy lock-store
    /// conditions the call completes well within it. The caller's <paramref name="cancellationToken"/> still
    /// takes precedence if it fires first. See issue #297 and the F#2 review finding from PR #284 for the rationale.
    /// </remarks>
    Task<IDistributedLease?> TryAcquireAsync(
        string resource,
        DistributedLockAcquireOptions? options = null,
        CancellationToken cancellationToken = default
    );

    /// <summary>
    /// Renews a resource lock for a specified <paramref name="resource"/> by extending
    /// the expiration time of the lock if it is still held to the <paramref name="leaseId"/>
    /// and return <see langword="true"/>, otherwise <see langword="false"/>.
    /// </summary>
    /// <param name="resource">The locked resource to renew. Must be non-null and non-whitespace.</param>
    /// <param name="leaseId">The lease id the lock must currently be held by for the renewal to apply.</param>
    /// <param name="timeUntilExpires">
    /// New lease duration from now. <see langword="null"/> applies <see cref="DefaultTimeUntilExpires"/>.
    /// </param>
    /// <param name="cancellationToken">Cancels the renewal; surfaces as <see cref="OperationCanceledException"/>.</param>
    /// <returns>
    /// <see langword="true"/> when the lease was still held by <paramref name="leaseId"/> and was extended;
    /// <see langword="false"/> when it was already lost (expired or taken over by another holder).
    /// </returns>
    /// <exception cref="ArgumentNullException"><paramref name="resource"/> or <paramref name="leaseId"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException"><paramref name="resource"/> or <paramref name="leaseId"/> is empty or whitespace.</exception>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was cancelled.</exception>
    Task<bool> RenewAsync(
        string resource,
        string leaseId,
        TimeSpan? timeUntilExpires = null,
        CancellationToken cancellationToken = default
    );

    /// <summary>
    /// Gets the current lease id for a specified <paramref name="resource"/>, or null when it is not locked
    /// or the backend cannot observe the current holder identity on its inspection path. This is an
    /// inspection/read primitive; it does not renew the lease. Consumers that already hold a monitored
    /// handle should prefer <see cref="IDistributedLease.LostToken"/> for lease-loss signals.
    /// </summary>
    /// <param name="resource">The resource to inspect. Must be non-null and non-whitespace.</param>
    /// <param name="cancellationToken">Cancels the read; surfaces as <see cref="OperationCanceledException"/>.</param>
    /// <returns>The current holder's lease id, or <see langword="null"/> when not locked or not observable.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="resource"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException"><paramref name="resource"/> is empty or whitespace.</exception>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was cancelled.</exception>
    Task<string?> GetLeaseIdAsync(string resource, CancellationToken cancellationToken = default);

    /// <summary>
    /// Releases a resource lock for a specified <paramref name="resource"/>
    /// if it is acquired by the <paramref name="leaseId"/>. Releasing a lock that is not held by
    /// <paramref name="leaseId"/> (already expired or taken over) is a no-op, not an error.
    /// </summary>
    /// <param name="resource">The locked resource to release. Must be non-null and non-whitespace.</param>
    /// <param name="leaseId">The lease id the lock must currently be held by for the release to apply.</param>
    /// <param name="cancellationToken">Cancels the release; surfaces as <see cref="OperationCanceledException"/>.</param>
    /// <exception cref="ArgumentNullException"><paramref name="resource"/> or <paramref name="leaseId"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException"><paramref name="resource"/> or <paramref name="leaseId"/> is empty or whitespace.</exception>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was cancelled.</exception>
    Task ReleaseAsync(string resource, string leaseId, CancellationToken cancellationToken = default);

    /// <summary>Checks if a specified resource is currently locked.</summary>
    /// <param name="resource">The resource to inspect. Must be non-null and non-whitespace.</param>
    /// <param name="cancellationToken">Cancels the read; surfaces as <see cref="OperationCanceledException"/>.</param>
    /// <returns><see langword="true"/> when the resource currently has an active lock; otherwise <see langword="false"/>.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="resource"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException"><paramref name="resource"/> is empty or whitespace.</exception>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was cancelled.</exception>
    Task<bool> IsLockedAsync(string resource, CancellationToken cancellationToken = default);

    /// <summary>Gets the remaining time until the lock expires for a specified resource.</summary>
    /// <param name="resource">The resource to inspect. Must be non-null and non-whitespace.</param>
    /// <param name="cancellationToken">Cancels the read; surfaces as <see cref="OperationCanceledException"/>.</param>
    /// <returns>The remaining TTL, or null if the resource is not locked or has no expiration.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="resource"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException"><paramref name="resource"/> is empty or whitespace.</exception>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was cancelled.</exception>
    Task<TimeSpan?> GetExpirationAsync(string resource, CancellationToken cancellationToken = default);

    /// <summary>Gets information about a specific lock.</summary>
    /// <param name="resource">The resource to inspect. Must be non-null and non-whitespace.</param>
    /// <param name="cancellationToken">Cancels the read; surfaces as <see cref="OperationCanceledException"/>.</param>
    /// <remarks>
    /// <see cref="DistributedLockInfo.LeaseId"/> may be null when the backend can observe that the
    /// resource is locked but cannot surface the current holder identity on the inspection path.
    /// </remarks>
    /// <returns>Lock information, or null if the resource is not locked.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="resource"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException"><paramref name="resource"/> is empty or whitespace.</exception>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was cancelled.</exception>
    Task<DistributedLockInfo?> GetLockInfoAsync(string resource, CancellationToken cancellationToken = default);

    /// <summary>Lists the active locks observable through this provider's inspection path.</summary>
    /// <param name="cancellationToken">Cancels the read; surfaces as <see cref="OperationCanceledException"/>.</param>
    /// <returns>Collection of active lock information.</returns>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was cancelled.</exception>
    Task<IReadOnlyList<DistributedLockInfo>> ListActiveLocksAsync(CancellationToken cancellationToken = default);

    /// <summary>Gets the total count of active locks observable through this provider's inspection path.</summary>
    /// <param name="cancellationToken">Cancels the read; surfaces as <see cref="OperationCanceledException"/>.</param>
    /// <returns>The number of active locks observable through this provider's inspection path.</returns>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was cancelled.</exception>
    Task<long> GetActiveLocksCountAsync(CancellationToken cancellationToken = default);
}
