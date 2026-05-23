// Copyright (c) Mahmoud Shaheen. All rights reserved.

#pragma warning disable IDE0130
// ReSharper disable once CheckNamespace
namespace Headless.DistributedLocks;

/// <summary>Per-call configuration for <see cref="IDistributedLockProvider.AcquireAsync"/> / <see cref="IDistributedLockProvider.TryAcquireAsync"/>.</summary>
/// <remarks>
/// All fields are optional; pass <see langword="null"/> (or omit the argument) to use defaults.
/// Records support <c>with</c> expressions for variants: <c>options with { Monitoring = LockMonitoringMode.AutoExtend }</c>.
/// </remarks>
[PublicAPI]
public sealed record DistributedLockAcquireOptions
{
    /// <summary>How long the storage row should be considered held before the lease expires. <see langword="null"/> uses the provider's finite default (20 minutes for the built-in providers). <see cref="Timeout.InfiniteTimeSpan"/> disables expiration only when monitoring is disabled; monitored modes require a finite lease duration.</summary>
    public TimeSpan? TimeUntilExpires { get; init; }

    /// <summary>Maximum time to wait for the lock to become available before <see cref="IDistributedLockProvider.AcquireAsync"/> throws or <see cref="IDistributedLockProvider.TryAcquireAsync"/> returns null. <see langword="null"/> uses the provider's default.</summary>
    public TimeSpan? AcquireTimeout { get; init; }

    /// <summary><see langword="true"/> (default) to release the lock when the handle is disposed; <see langword="false"/> to require explicit <see cref="IDistributedLock.ReleaseAsync"/>. Use <see langword="false"/> when the caller needs to bound release time with their own <see cref="CancellationToken"/>.</summary>
    public bool ReleaseOnDispose { get; init; } = true;

    /// <summary>Controls whether and how the lease is monitored for liveness. See <see cref="LockMonitoringMode"/> for per-value behavior. <see cref="LockMonitoringMode.Monitor"/> and <see cref="LockMonitoringMode.AutoExtend"/> require a finite lease duration; <see langword="null"/> uses the provider default, while <see cref="Timeout.InfiniteTimeSpan"/> throws <see cref="ArgumentException"/>.</summary>
    public LockMonitoringMode Monitoring { get; init; } = LockMonitoringMode.None;
}
