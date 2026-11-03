// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.DistributedLocks;

/// <summary>Per-call configuration for <see cref="IDistributedLock.AcquireAsync"/> / <see cref="IDistributedLock.TryAcquireAsync"/>.</summary>
/// <remarks>
/// All fields are optional; pass <see langword="null"/> (or omit the argument) to use defaults.
/// Records support <c>with</c> expressions for variants: <c>options with { Monitoring = LockMonitoringMode.AutoExtend }</c>.
/// </remarks>
[PublicAPI]
public sealed record DistributedLockAcquireOptions
{
    /// <summary>How long the storage row should be considered held before the lease expires. <see langword="null"/> uses the provider's finite default (20 minutes for the built-in providers). <see cref="Timeout.InfiniteTimeSpan"/> disables expiration only when monitoring is disabled; monitored modes require a finite lease duration.</summary>
    public TimeSpan? TimeUntilExpires { get; init; }

    /// <summary>Maximum time to wait for the lock to become available before <see cref="IDistributedLock.AcquireAsync"/> throws or <see cref="IDistributedLock.TryAcquireAsync"/> returns null. <see langword="null"/> uses the provider's default.</summary>
    public TimeSpan? AcquireTimeout { get; init; }

    /// <summary><see langword="true"/> (default) to release the lock when the handle is disposed; <see langword="false"/> to require explicit <see cref="IDistributedLease.ReleaseAsync"/>. Use <see langword="false"/> when the caller needs to bound release time with their own <see cref="CancellationToken"/>.</summary>
    public bool ReleaseOnDispose { get; init; } = true;

    /// <summary>Controls whether and how the lease is monitored for liveness. See <see cref="LockMonitoringMode"/> for per-value behavior. <see cref="LockMonitoringMode.Monitor"/> and <see cref="LockMonitoringMode.AutoExtend"/> require a finite lease duration; <see langword="null"/> uses the provider default, while <see cref="Timeout.InfiniteTimeSpan"/> throws <see cref="ArgumentException"/>.</summary>
    public LockMonitoringMode Monitoring { get; init; } = LockMonitoringMode.None;

    /// <summary>
    /// <see langword="true"/> (default) to send the release wake-up when this lease is released, so acquirers waiting
    /// on the resource retry at once instead of on their next poll; <see langword="false"/> to skip it.
    /// </summary>
    /// <remarks>
    /// The wake-up costs one signal per release: on the storage-backed providers a messaging broadcast that every
    /// instance receives, on PostgreSQL a <c>pg_notify</c>. Skip it only for a resource that no caller ever waits on,
    /// where every acquirer uses a zero <see cref="AcquireTimeout"/> (a try-once lock such as a periodic task that
    /// one instance runs per tick). A waiter that does exist still acquires the lock, on its next poll. Setting
    /// <see langword="false"/> with any <see cref="AcquireTimeout"/> other than <see cref="TimeSpan.Zero"/> throws
    /// <see cref="ArgumentException"/> at acquire, because a caller that waits implies others may wait too.
    /// </remarks>
    public bool NotifyWaitersOnRelease { get; init; } = true;
}
