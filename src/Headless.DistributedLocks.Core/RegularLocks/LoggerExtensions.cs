// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Microsoft.Extensions.Logging;

#pragma warning disable IDE0130 // ReSharper disable once CheckNamespace
namespace Headless.DistributedLocks;

public static partial class RegularLockLoggerExtensions
{
    public static void LogErrorAcquiringLockElapsed(
        this ILogger logger,
        Exception exception,
        string resource,
        string leaseId,
        TimeProvider timeProvider,
        long timestamp
    )
    {
        if (!logger.IsEnabled(LogLevel.Trace))
        {
            return;
        }

        LogErrorAcquiringLock(logger, exception, resource, leaseId, timeProvider.GetElapsedTime(timestamp));
    }

    [LoggerMessage(
        EventId = 1,
        EventName = "LockReleaseStarted",
        Level = LogLevel.Trace,
        Message = "ReleaseAsync Start: R={Resource} Id={LeaseId}"
    )]
    public static partial void LogReleaseStarted(this ILogger logger, string resource, string leaseId);

    [LoggerMessage(
        EventId = 2,
        EventName = "LockReleaseReleased",
        Level = LogLevel.Debug,
        Message = "Released lock: R={Resource} Id={LeaseId}"
    )]
    public static partial void LogReleaseReleased(this ILogger logger, string resource, string leaseId);

    [LoggerMessage(
        EventId = 3,
        EventName = "RenewingLock",
        Level = LogLevel.Debug,
        Message = "Renewing lock: R={Resource} Id={LeaseId} for {Duration:g}"
    )]
    public static partial void LogRenewingLock(
        this ILogger logger,
        string resource,
        string leaseId,
        TimeSpan? duration
    );

    [LoggerMessage(
        EventId = 4,
        EventName = "GotLockReleasedMessage",
        Level = LogLevel.Trace,
        Message = "Got lock released message: R={Resource} Id={LeaseId}"
    )]
    public static partial void LogGotLockReleasedMessage(this ILogger logger, string resource, string leaseId);

    [LoggerMessage(
        EventId = 5,
        EventName = "AttemptingToAcquireLock",
        Level = LogLevel.Debug,
        Message = "Attempting to acquire lock: R={Resource} Id={LeaseId}"
    )]
    public static partial void LogAttemptingToAcquireLock(this ILogger logger, string resource, string leaseId);

    [LoggerMessage(
        EventId = 6,
        EventName = "ErrorAcquiringLock",
        Level = LogLevel.Trace,
        Message = "Error acquiring lock: R={Resource} Id={LeaseId} after {Duration:g}"
    )]
    public static partial void LogErrorAcquiringLock(
        this ILogger logger,
        Exception exception,
        string resource,
        string leaseId,
        TimeSpan duration
    );

    [LoggerMessage(
        EventId = 7,
        EventName = "FailedToAcquireLock",
        Level = LogLevel.Debug,
        Message = "Failed to acquire lock: {Resource} Id={LeaseId}"
    )]
    public static partial void LogFailedToAcquireLock(this ILogger logger, string resource, string leaseId);

    [LoggerMessage(
        EventId = 8,
        EventName = "CancellationRequested",
        Level = LogLevel.Trace,
        Message = "Cancellation requested while acquiring lock: R={Resource} Id={LeaseId}"
    )]
    public static partial void LogCancellationRequested(this ILogger logger, string resource, string leaseId);

    [LoggerMessage(
        EventId = 9,
        EventName = "CancellationRequestedForLock",
        Level = LogLevel.Trace,
        Message = "Cancellation requested for lock R={Resource} Id={LeaseId} after {Duration:g}"
    )]
    public static partial void LogCancellationRequestedAfter(
        this ILogger logger,
        string resource,
        string leaseId,
        TimeSpan duration
    );

    [LoggerMessage(
        EventId = 10,
        EventName = "DelayBeforeRetry",
        Level = LogLevel.Trace,
        Message = "Will wait {Delay:g} before retrying to acquire lock: R={Resource} Id={LeaseId}"
    )]
    public static partial void LogDelayBeforeRetry(
        this ILogger logger,
        string resource,
        string leaseId,
        TimeSpan delay
    );

    [LoggerMessage(
        EventId = 11,
        EventName = "LongLockAcquired",
        Level = LogLevel.Warning,
        Message = "Acquired lock in long duration R={Resource} Id={LeaseId} after {Duration:g}"
    )]
    public static partial void LogLongLockAcquired(
        this ILogger logger,
        string resource,
        string leaseId,
        TimeSpan duration
    );

    [LoggerMessage(
        EventId = 12,
        EventName = "AcquiredLock",
        Level = LogLevel.Debug,
        Message = "Acquired lock: R={Resource} Id={LeaseId} after {Duration:g}"
    )]
    public static partial void LogAcquiredLock(this ILogger logger, string resource, string leaseId, TimeSpan duration);

    [LoggerMessage(
        EventId = 13,
        EventName = "FailedToAcquireLockAfter",
        Level = LogLevel.Warning,
        Message = "Failed to acquire lock: R={Resource} Id={LeaseId} after {Duration:g}"
    )]
    public static partial void LogFailedToAcquireLockAfter(
        this ILogger logger,
        string resource,
        string leaseId,
        TimeSpan duration
    );

    [LoggerMessage(
        EventId = 14,
        EventName = "ProcessingLockReleased",
        Level = LogLevel.Debug,
        Message = "Processing lock released {MessageId} for {Resource}"
    )]
    public static partial void LogProcessingLockReleased(this ILogger logger, string? messageId, string resource);

    [LoggerMessage(
        EventId = 15,
        EventName = "BestEffortLockCleanupFailed",
        Level = LogLevel.Warning,
        Message = "Best-effort cleanup failed for potentially orphaned lock: R={Resource} Id={LeaseId}. Lock will expire via TTL."
    )]
    public static partial void LogBestEffortLockCleanupFailed(
        this ILogger logger,
        Exception exception,
        string resource,
        string leaseId
    );

    [LoggerMessage(
        EventId = 16,
        EventName = "OutboxBusAbsent",
        Level = LogLevel.Warning,
        Message = "No IOutboxBus registered; lock-release wake-ups will fall back to polling backoff. Register Headless.Messaging for push-based latency."
    )]
    public static partial void LogOutboxBusAbsent(this ILogger logger);

    [LoggerMessage(
        EventId = 17,
        EventName = "LockReleasePublishFailed",
        Level = LogLevel.Warning,
        Message = "Lock released but outbox publish failed; waiters will fall back to polling: R={Resource} Id={LeaseId}"
    )]
    public static partial void LogLockReleasePublishFailed(
        this ILogger logger,
        Exception exception,
        string resource,
        string leaseId
    );

    [LoggerMessage(
        EventId = 19,
        EventName = "LockStorageRetry",
        Level = LogLevel.Warning,
        Message = "Retrying lock storage operation (attempt {Attempt}) after {Delay:g}"
    )]
    public static partial void LogLockStorageRetry(
        this ILogger logger,
        int attempt,
        TimeSpan delay,
        Exception? exception
    );

    [LoggerMessage(
        EventId = 20,
        EventName = "LeaseMonitorRegistered",
        Level = LogLevel.Trace,
        Message = "Registered lease monitor: R={Resource} Id={LeaseId}"
    )]
    public static partial void LogLeaseMonitorRegistered(this ILogger logger, string resource, string leaseId);

    [LoggerMessage(
        EventId = 21,
        EventName = "LeaseMonitorDeregistered",
        Level = LogLevel.Trace,
        Message = "Deregistered lease monitor: R={Resource} Id={LeaseId}"
    )]
    public static partial void LogLeaseMonitorDeregistered(this ILogger logger, string resource, string leaseId);

    [LoggerMessage(
        EventId = 22,
        EventName = "LeaseMonitorNudged",
        Level = LogLevel.Trace,
        Message = "Nudged lease monitor: R={Resource} Id={LeaseId}"
    )]
    public static partial void LogLeaseMonitorNudged(this ILogger logger, string resource, string leaseId);

    [LoggerMessage(
        EventId = 23,
        EventName = "LockReleaseTimedOut",
        Level = LogLevel.Warning,
        Message = "Release pipeline exceeded DisposeTimeout ({Timeout:g}) for R={Resource} Id={LeaseId}; storage cleanup continues in background and the per-record TTL is the eventual consistency mechanism."
    )]
    public static partial void LogLockReleaseTimedOut(
        this ILogger logger,
        string resource,
        string leaseId,
        TimeSpan timeout
    );
}
