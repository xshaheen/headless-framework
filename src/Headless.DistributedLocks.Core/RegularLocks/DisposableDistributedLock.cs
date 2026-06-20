// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Microsoft.Extensions.Logging;

#pragma warning disable IDE0130 // ReSharper disable once CheckNamespace
namespace Headless.DistributedLocks;

/// <summary>
/// The concrete <see cref="DistributedLockHandleBase"/> for mutex (exclusive) locks acquired through
/// <see cref="DistributedLock"/>. Delegates all storage operations back to the originating
/// <see cref="IDistributedLock"/> provider, which applies the retry pipeline and key scoping.
/// </summary>
internal sealed class DisposableDistributedLock : DistributedLockHandleBase
{
    private readonly IDistributedLock _lockProvider;

    /// <summary>
    /// Creates a new mutex lock handle. Ownership must have already been acquired in storage before
    /// constructing this handle; the constructor does not perform any storage operations.
    /// </summary>
    /// <param name="resource">The resource name for which the lock was acquired.</param>
    /// <param name="leaseId">The unique lease identifier assigned to this holder.</param>
    /// <param name="fencingToken">The fencing token issued by the backend, or <see langword="null"/> if unsupported.</param>
    /// <param name="leaseDuration">The finite TTL of the lease (used for monitoring cadence calculation).</param>
    /// <param name="timeWaitedForLock">The wall-clock time spent waiting before the lock was granted.</param>
    /// <param name="lockProvider">The provider used for renew, validate, and release operations.</param>
    /// <param name="releaseOnDispose">When <see langword="true"/>, <see cref="DistributedLockHandleBase.DisposeAsync"/> calls <see cref="DistributedLockHandleBase.ReleaseAsync"/>.</param>
    /// <param name="autoExtend">When <see langword="true"/>, the attached monitor extends the TTL at each cadence tick instead of only validating.</param>
    /// <param name="options">Lock configuration used to compute monitoring cadence and dispose timeout.</param>
    /// <param name="timeProvider">Time provider for elapsed-time and timestamp measurements.</param>
    /// <param name="deregisterMonitor">Callback invoked after the monitor is disposed to remove it from the provider's registry.</param>
    /// <param name="logger">Logger for dispose, renew, and monitor lifecycle events.</param>
    internal DisposableDistributedLock(
        string resource,
        string leaseId,
        long? fencingToken,
        TimeSpan leaseDuration,
        TimeSpan timeWaitedForLock,
        IDistributedLock lockProvider,
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
            releaseOnDispose,
            autoExtend,
            options,
            timeProvider,
            deregisterMonitor,
            logger
        )
    {
        _lockProvider = lockProvider;
    }

    /// <summary>
    /// Extends the lease TTL in storage via the owning <see cref="IDistributedLock"/> provider,
    /// incrementing the renewal counter on success.
    /// </summary>
    /// <param name="timeUntilExpires">
    /// The new TTL from now. <see langword="null"/> applies the provider's
    /// <see cref="IDistributedLock.DefaultTimeUntilExpires"/>.
    /// </param>
    /// <param name="cancellationToken">Token to cancel the renewal storage call.</param>
    /// <returns>
    /// <see langword="true"/> when the lease was found in storage and the TTL was extended;
    /// <see langword="false"/> when the lease is no longer held (expired or released by another path).
    /// </returns>
    /// <exception cref="OperationCanceledException">Thrown when <paramref name="cancellationToken"/> is cancelled.</exception>
    public override async Task<bool> RenewAsync(
        TimeSpan? timeUntilExpires = null,
        CancellationToken cancellationToken = default
    )
    {
        if (Logger.IsEnabled(LogLevel.Trace))
        {
            Logger.LogDisposableLockRenewing(Resource, LeaseId);
        }

        var result = await _lockProvider
            .RenewAsync(Resource, LeaseId, timeUntilExpires, cancellationToken)
            .ConfigureAwait(false);

        if (!result)
        {
            Logger.LogDisposableLockRenewFailed(Resource, LeaseId);

            return false;
        }

        IncrementRenewalCount();

        if (Logger.IsEnabled(LogLevel.Debug))
        {
            Logger.LogDisposableLockRenewed(Resource, LeaseId);
        }

        return true;
    }

    protected override Task<bool> RenewLeaseAsync(CancellationToken cancellationToken)
    {
        return _lockProvider.RenewAsync(Resource, LeaseId, LeaseDuration, cancellationToken);
    }

    protected override async Task<bool> ValidateOwnershipAsync(CancellationToken cancellationToken)
    {
        var currentLockId = await _lockProvider.GetLeaseIdAsync(Resource, cancellationToken).ConfigureAwait(false);

        return string.Equals(currentLockId, LeaseId, StringComparison.Ordinal);
    }

    protected override Task ReleaseCoreAsync()
    {
        return _lockProvider.ReleaseAsync(Resource, LeaseId, CancellationToken.None);
    }

    protected override void OnMonitorDisposeFailed(Exception exception)
    {
        Logger.LogDisposableLockMonitorDisposeFailed(exception, Resource, LeaseId);
    }
}

internal static partial class DisposableDistributedLockLog
{
    /// <summary>Logs the start of a manual renewal attempt (Trace).</summary>
    [LoggerMessage(
        EventId = 1,
        EventName = "DisposableLockRenewing",
        Level = LogLevel.Trace,
        Message = "Renewing lock {Resource} ({LeaseId})"
    )]
    public static partial void LogDisposableLockRenewing(this ILogger logger, string resource, string leaseId);

    /// <summary>Logs that a renewal attempt found the lease absent from storage (Debug).</summary>
    [LoggerMessage(
        EventId = 2,
        EventName = "DisposableLockRenewFailed",
        Level = LogLevel.Debug,
        Message = "Unable to renew lock {Resource} ({LeaseId})"
    )]
    public static partial void LogDisposableLockRenewFailed(this ILogger logger, string resource, string leaseId);

    /// <summary>Logs a successful manual renewal (Debug).</summary>
    [LoggerMessage(
        EventId = 3,
        EventName = "DisposableLockRenewed",
        Level = LogLevel.Debug,
        Message = "Renewed lock {Resource} ({LeaseId})"
    )]
    public static partial void LogDisposableLockRenewed(this ILogger logger, string resource, string leaseId);

    /// <summary>Logs the start of the storage release call during dispose (Debug).</summary>
    [LoggerMessage(
        EventId = 4,
        EventName = "DisposableLockReleasing",
        Level = LogLevel.Debug,
        Message = "Releasing lock: R={Resource} Id={LeaseId} after {Duration:g}"
    )]
    public static partial void LogDisposableLockReleasing(
        this ILogger logger,
        string resource,
        string leaseId,
        TimeSpan duration
    );

    /// <summary>Logs the start of DisposeAsync (Trace).</summary>
    [LoggerMessage(
        EventId = 5,
        EventName = "DisposableLockDisposing",
        Level = LogLevel.Trace,
        Message = "Disposing lock: R={Resource} Id={LeaseId}"
    )]
    public static partial void LogDisposableLockDisposing(this ILogger logger, string resource, string leaseId);

    /// <summary>Logs a failure during the dispose-time release call (Error).</summary>
    [LoggerMessage(
        EventId = 6,
        EventName = "DisposableLockReleaseFailed",
        Level = LogLevel.Error,
        Message = "Unable to release lock: R={Resource} Id={LeaseId}"
    )]
    public static partial void LogDisposableLockReleaseFailed(
        this ILogger logger,
        Exception exception,
        string resource,
        string leaseId
    );

    /// <summary>Logs completion of DisposeAsync (Trace).</summary>
    [LoggerMessage(
        EventId = 7,
        EventName = "DisposableLockDisposed",
        Level = LogLevel.Trace,
        Message = "Disposed lock: R={Resource} Id={LeaseId}"
    )]
    public static partial void LogDisposableLockDisposed(this ILogger logger, string resource, string leaseId);

    /// <summary>Logs a failure to dispose the attached lease monitor before releasing the lock (Warning).</summary>
    [LoggerMessage(
        EventId = 8,
        EventName = "DisposableLockMonitorDisposeFailed",
        Level = LogLevel.Warning,
        Message = "Unable to dispose lease monitor before releasing lock: R={Resource} Id={LeaseId}"
    )]
    public static partial void LogDisposableLockMonitorDisposeFailed(
        this ILogger logger,
        Exception exception,
        string resource,
        string leaseId
    );
}
