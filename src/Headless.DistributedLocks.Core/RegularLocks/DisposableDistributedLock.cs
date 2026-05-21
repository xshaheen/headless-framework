// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Microsoft.Extensions.Logging;
using Nito.AsyncEx;

#pragma warning disable IDE0130
// ReSharper disable once CheckNamespace
namespace Headless.DistributedLocks;

public sealed class DisposableDistributedLock(
    string resource,
    string lockId,
    TimeSpan timeWaitedForLock,
    IDistributedLockProvider lockProvider,
    bool releaseOnDispose,
    TimeProvider timeProvider,
    ILogger logger
) : IDistributedLock
{
    private volatile bool _isReleased;
    private readonly AsyncLock _lock = new();
    private readonly long _timestamp = timeProvider.GetTimestamp();

    public string LockId { get; } = lockId;

    public string Resource { get; } = resource;

    public DateTimeOffset DateAcquired { get; } = timeProvider.GetUtcNow();

    public TimeSpan TimeWaitedForLock { get; } = timeWaitedForLock;

    public int RenewalCount { get; private set; }

    public async Task<bool> RenewAsync(TimeSpan? timeUntilExpires = null, CancellationToken cancellationToken = default)
    {
        if (logger.IsEnabled(LogLevel.Trace))
        {
            logger.LogDisposableLockRenewing(Resource, LockId);
        }

        var result = await lockProvider
            .RenewAsync(Resource, LockId, timeUntilExpires, cancellationToken)
            .ConfigureAwait(false);

        if (!result)
        {
            logger.LogDisposableLockRenewFailed(Resource, LockId);

            return false;
        }

        RenewalCount++;

        if (logger.IsEnabled(LogLevel.Debug))
        {
            logger.LogDisposableLockRenewed(Resource, LockId);
        }

        return true;
    }

    public async Task ReleaseAsync()
    {
        if (_isReleased)
        {
            return;
        }

        using (await _lock.LockAsync(CancellationToken.None).ConfigureAwait(false))
        {
            if (_isReleased)
            {
                return;
            }

            _isReleased = true;

            if (logger.IsEnabled(LogLevel.Debug))
            {
                var elapsed = timeProvider.GetElapsedTime(_timestamp);

                logger.LogDisposableLockReleasing(Resource, LockId, elapsed);
            }

            await lockProvider.ReleaseAsync(Resource, LockId, CancellationToken.None).ConfigureAwait(false);
        }
    }

    public async ValueTask DisposeAsync()
    {
        var isTraceLogLevelEnabled = logger.IsEnabled(LogLevel.Trace);

        if (isTraceLogLevelEnabled)
        {
            logger.LogDisposableLockDisposing(Resource, LockId);
        }

        try
        {
            if (releaseOnDispose)
            {
                await ReleaseAsync().ConfigureAwait(false);
            }
        }
        catch (Exception e)
        {
            if (logger.IsEnabled(LogLevel.Error))
            {
                logger.LogDisposableLockReleaseFailed(e, Resource, LockId);
            }
        }

        if (isTraceLogLevelEnabled)
        {
            logger.LogDisposableLockDisposed(Resource, LockId);
        }
    }
}

internal static partial class DisposableDistributedLockLog
{
    [LoggerMessage(
        EventId = 1,
        EventName = "DisposableLockRenewing",
        Level = LogLevel.Trace,
        Message = "Renewing lock {Resource} ({LockId})"
    )]
    public static partial void LogDisposableLockRenewing(this ILogger logger, string resource, string lockId);

    [LoggerMessage(
        EventId = 2,
        EventName = "DisposableLockRenewFailed",
        Level = LogLevel.Debug,
        Message = "Unable to renew lock {Resource} ({LockId})"
    )]
    public static partial void LogDisposableLockRenewFailed(this ILogger logger, string resource, string lockId);

    [LoggerMessage(
        EventId = 3,
        EventName = "DisposableLockRenewed",
        Level = LogLevel.Debug,
        Message = "Renewed lock {Resource} ({LockId})"
    )]
    public static partial void LogDisposableLockRenewed(this ILogger logger, string resource, string lockId);

    [LoggerMessage(
        EventId = 4,
        EventName = "DisposableLockReleasing",
        Level = LogLevel.Debug,
        Message = "Releasing lock: R={Resource} Id={LockId} after {Duration:g}"
    )]
    public static partial void LogDisposableLockReleasing(
        this ILogger logger,
        string resource,
        string lockId,
        TimeSpan duration
    );

    [LoggerMessage(
        EventId = 5,
        EventName = "DisposableLockDisposing",
        Level = LogLevel.Trace,
        Message = "Disposing lock: R={Resource} Id={LockId}"
    )]
    public static partial void LogDisposableLockDisposing(this ILogger logger, string resource, string lockId);

    [LoggerMessage(
        EventId = 6,
        EventName = "DisposableLockReleaseFailed",
        Level = LogLevel.Error,
        Message = "Unable to release lock: R={Resource} Id={LockId}"
    )]
    public static partial void LogDisposableLockReleaseFailed(
        this ILogger logger,
        Exception exception,
        string resource,
        string lockId
    );

    [LoggerMessage(
        EventId = 7,
        EventName = "DisposableLockDisposed",
        Level = LogLevel.Trace,
        Message = "Disposed lock: R={Resource} Id={LockId}"
    )]
    public static partial void LogDisposableLockDisposed(this ILogger logger, string resource, string lockId);
}
