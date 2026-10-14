// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Microsoft.Extensions.Logging;

namespace Headless.DistributedLocks;

/// <summary>
/// The concrete <see cref="DistributedLockHandleBase"/> for a semaphore slot acquired through
/// <see cref="DistributedSemaphoreProvider"/>. Delegates all storage operations back to the provider.
/// </summary>
internal sealed class DisposableSemaphoreSlot : DistributedLockHandleBase
{
    private readonly DistributedSemaphoreProvider _provider;

    /// <summary>
    /// Creates a new semaphore slot handle. Ownership must have already been acquired in storage
    /// before constructing this handle; the constructor does not perform any storage operations.
    /// </summary>
    /// <param name="resource">The semaphore resource name.</param>
    /// <param name="leaseId">The unique slot identifier assigned at acquire time.</param>
    /// <param name="fencingToken">The fencing token issued by the backend, or <see langword="null"/> if unsupported.</param>
    /// <param name="leaseDuration">The finite TTL of the slot (used for monitoring cadence calculation).</param>
    /// <param name="timeWaitedForLock">The wall-clock time spent waiting before the slot was granted.</param>
    /// <param name="acquireTimestamp">Timestamp taken immediately before the storage call that granted the slot.</param>
    /// <param name="provider">The provider used for extend, validate, and release operations.</param>
    /// <param name="releaseOnDispose">When <see langword="true"/>, <see cref="DistributedLockHandleBase.DisposeAsync"/> calls <see cref="DistributedLockHandleBase.ReleaseAsync"/>.</param>
    /// <param name="autoExtend">When <see langword="true"/>, the attached monitor extends the slot TTL at each cadence tick.</param>
    /// <param name="options">Lock configuration used to compute monitoring cadence and dispose timeout.</param>
    /// <param name="timeProvider">Time provider for elapsed-time and timestamp measurements.</param>
    /// <param name="deregisterMonitor">Callback invoked after the monitor is disposed to remove it from the provider registry.</param>
    /// <param name="logger">Logger for dispose, renew, and monitor lifecycle events.</param>
    internal DisposableSemaphoreSlot(
        string resource,
        string leaseId,
        LockFencingToken? fencingToken,
        TimeSpan leaseDuration,
        TimeSpan timeWaitedForLock,
        long acquireTimestamp,
        DistributedSemaphoreProvider provider,
        bool releaseOnDispose,
        bool autoExtend,
        DistributedLockOptions options,
        TimeProvider timeProvider,
        Action<string, string>? deregisterMonitor,
        ILogger logger
    )
        : base(
            resource,
            leaseId,
            fencingToken,
            leaseDuration,
            timeWaitedForLock,
            acquireTimestamp,
            releaseOnDispose,
            autoExtend,
            options,
            timeProvider,
            deregisterMonitor,
            logger
        )
    {
        _provider = provider;
    }

    /// <summary>
    /// Extends the slot TTL in storage via the owning <see cref="DistributedSemaphoreProvider"/>.
    /// <see langword="null"/> applies the provider's default TTL.
    /// </summary>
    protected override Task<bool> RenewCoreAsync(TimeSpan? timeUntilExpires, CancellationToken cancellationToken)
    {
        return _provider.RenewAsync(Resource, LeaseId, timeUntilExpires, cancellationToken);
    }

    // A slot is stored with a finite expiry score, so the provider maps both null and infinite to its default.
    protected override TimeSpan? GetAppliedRenewTtl(TimeSpan? timeUntilExpires)
    {
        return timeUntilExpires is { } ttl && ttl != Timeout.InfiniteTimeSpan ? ttl : _provider.DefaultTimeUntilExpires;
    }

    protected override Task<bool> RenewLeaseAsync(CancellationToken cancellationToken)
    {
        return _provider.RenewAsync(Resource, LeaseId, LeaseDuration, cancellationToken);
    }

    protected override Task<bool> ValidateOwnershipAsync(CancellationToken cancellationToken)
    {
        return _provider.ValidateAsync(Resource, LeaseId, cancellationToken);
    }

    protected override Task ReleaseCoreAsync()
    {
        return _provider.ReleaseAsync(Resource, LeaseId, CancellationToken.None);
    }

    protected override void OnMonitorDisposeFailed(Exception exception)
    {
        Logger.LogDisposableLockMonitorDisposeFailed(exception, Resource, LeaseId);
    }
}
