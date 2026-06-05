// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Microsoft.Extensions.Logging;

#pragma warning disable IDE0130 // ReSharper disable once CheckNamespace
namespace Headless.DistributedLocks;

internal sealed class DisposableDistributedLock : DistributedLockHandleBase
{
    private readonly IDistributedLock _lockProvider;

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
    [LoggerMessage(
        EventId = 1,
        EventName = "DisposableLockRenewing",
        Level = LogLevel.Trace,
        Message = "Renewing lock {Resource} ({LeaseId})"
    )]
    public static partial void LogDisposableLockRenewing(this ILogger logger, string resource, string leaseId);

    [LoggerMessage(
        EventId = 2,
        EventName = "DisposableLockRenewFailed",
        Level = LogLevel.Debug,
        Message = "Unable to renew lock {Resource} ({LeaseId})"
    )]
    public static partial void LogDisposableLockRenewFailed(this ILogger logger, string resource, string leaseId);

    [LoggerMessage(
        EventId = 3,
        EventName = "DisposableLockRenewed",
        Level = LogLevel.Debug,
        Message = "Renewed lock {Resource} ({LeaseId})"
    )]
    public static partial void LogDisposableLockRenewed(this ILogger logger, string resource, string leaseId);

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

    [LoggerMessage(
        EventId = 5,
        EventName = "DisposableLockDisposing",
        Level = LogLevel.Trace,
        Message = "Disposing lock: R={Resource} Id={LeaseId}"
    )]
    public static partial void LogDisposableLockDisposing(this ILogger logger, string resource, string leaseId);

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

    [LoggerMessage(
        EventId = 7,
        EventName = "DisposableLockDisposed",
        Level = LogLevel.Trace,
        Message = "Disposed lock: R={Resource} Id={LeaseId}"
    )]
    public static partial void LogDisposableLockDisposed(this ILogger logger, string resource, string leaseId);

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
