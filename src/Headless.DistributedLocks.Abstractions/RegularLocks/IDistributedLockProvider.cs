// Copyright (c) Mahmoud Shaheen. All rights reserved.

#pragma warning disable IDE0130 // ReSharper disable once CheckNamespace
namespace Headless.DistributedLocks;

/// <summary>Provides methods to acquire, release, and manage resource locks.</summary>
[PublicAPI]
public interface IDistributedLockProvider
{
    TimeSpan DefaultTimeUntilExpires { get; }

    TimeSpan DefaultAcquireTimeout { get; }

    /// <summary>
    /// Acquires a resource lock for a specified resource and throws
    /// <see cref="LockAcquisitionTimeoutException"/> if the lock is not acquired before
    /// <see cref="DistributedLockAcquireOptions.AcquireTimeout"/> is reached.
    /// </summary>
    /// <param name="resource">The resource to acquire the lock for.</param>
    /// <param name="options">
    /// Per-call configuration (lease TTL, acquire timeout, release-on-dispose, monitoring mode).
    /// <see langword="null"/> applies the provider defaults. See <see cref="DistributedLockAcquireOptions"/>.
    /// </param>
    /// <param name="cancellationToken"></param>
    /// <exception cref="ArgumentException">
    /// Thrown when <see cref="DistributedLockAcquireOptions.Monitoring"/> is
    /// <see cref="LockMonitoringMode.Monitor"/> or <see cref="LockMonitoringMode.AutoExtend"/> but
    /// <see cref="DistributedLockAcquireOptions.TimeUntilExpires"/> is <see cref="Timeout.InfiniteTimeSpan"/>
    /// (monitoring requires a finite lease).
    /// </exception>
    Task<IDistributedLock> AcquireAsync(
        string resource,
        DistributedLockAcquireOptions? options = null,
        CancellationToken cancellationToken = default
    );

    /// <summary>
    /// Acquires a resource lock for a specified resource this method will block
    /// until the lock is acquired or the <see cref="DistributedLockAcquireOptions.AcquireTimeout"/> is reached.
    /// </summary>
    /// <param name="resource">The resource to acquire the lock for.</param>
    /// <param name="options">
    /// Per-call configuration (lease TTL, acquire timeout, release-on-dispose, monitoring mode).
    /// <see langword="null"/> applies the provider defaults. See <see cref="DistributedLockAcquireOptions"/>.
    /// </param>
    /// <param name="cancellationToken"></param>
    /// <returns>
    /// A task that represents the asynchronous operation.
    /// The task result contains the acquired lock or null if the lock could not be acquired.
    /// </returns>
    /// <exception cref="ArgumentException">
    /// Thrown when <see cref="DistributedLockAcquireOptions.Monitoring"/> is
    /// <see cref="LockMonitoringMode.Monitor"/> or <see cref="LockMonitoringMode.AutoExtend"/> but
    /// <see cref="DistributedLockAcquireOptions.TimeUntilExpires"/> is <see cref="Timeout.InfiniteTimeSpan"/>
    /// (monitoring requires a finite lease).
    /// </exception>
    /// <remarks>
    /// When <see cref="DistributedLockAcquireOptions.AcquireTimeout"/> is <see cref="TimeSpan.Zero"/> the
    /// implementation runs a single storage attempt with no retry loop (the "try once, no wait" semantic).
    /// The attempt is bounded by an internal safety deadline so a stalled lock-store call cannot hang the
    /// caller indefinitely, even when <paramref name="cancellationToken"/> is <see cref="CancellationToken.None"/>.
    /// The deadline is a ceiling on the storage round-trip, not a wait budget — under healthy lock-store
    /// conditions the call completes well within it. The caller's <paramref name="cancellationToken"/> still
    /// takes precedence if it fires first. See issue #297 and the F#2 review finding from PR #284 for the rationale.
    /// </remarks>
    Task<IDistributedLock?> TryAcquireAsync(
        string resource,
        DistributedLockAcquireOptions? options = null,
        CancellationToken cancellationToken = default
    );

    /// <summary>
    /// Renews a resource lock for a specified <paramref name="resource"/> by extending
    /// the expiration time of the lock if it is still held to the <paramref name="lockId"/>
    /// and return <see langword="true"/>, otherwise <see langword="false"/>.
    /// </summary>
    Task<bool> RenewAsync(
        string resource,
        string lockId,
        TimeSpan? timeUntilExpires = null,
        CancellationToken cancellationToken = default
    );

    /// <summary>
    /// Gets the current lock id for a specified <paramref name="resource"/>, or null when it is not locked.
    /// This is an inspection/read primitive; it does not renew the lease. Consumers that already hold a
    /// monitored handle should prefer <see cref="IDistributedLock.HandleLostToken"/> for lease-loss signals.
    /// </summary>
    Task<string?> GetLockIdAsync(string resource, CancellationToken cancellationToken = default);

    /// <summary>
    /// Releases a resource lock for a specified <paramref name="resource"/>
    /// if it is acquired by the <paramref name="lockId"/>.
    /// </summary>
    Task ReleaseAsync(string resource, string lockId, CancellationToken cancellationToken = default);

    /// <summary>Checks if a specified resource is currently locked.</summary>
    Task<bool> IsLockedAsync(string resource, CancellationToken cancellationToken = default);

    /// <summary>Gets the remaining time until the lock expires for a specified resource.</summary>
    /// <returns>The remaining TTL, or null if the resource is not locked or has no expiration.</returns>
    Task<TimeSpan?> GetExpirationAsync(string resource, CancellationToken cancellationToken = default);

    /// <summary>Gets information about a specific lock.</summary>
    /// <returns>Lock information, or null if the resource is not locked.</returns>
    Task<LockInfo?> GetLockInfoAsync(string resource, CancellationToken cancellationToken = default);

    /// <summary>Lists all active locks.</summary>
    /// <returns>Collection of active lock information.</returns>
    Task<IReadOnlyList<LockInfo>> ListActiveLocksAsync(CancellationToken cancellationToken = default);

    /// <summary>Gets the total count of active locks.</summary>
    Task<long> GetActiveLocksCountAsync(CancellationToken cancellationToken = default);
}
