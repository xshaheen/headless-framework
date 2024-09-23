// Copyright (c) Mahmoud Shaheen, 2024. All rights reserved

using Microsoft.Extensions.Logging;

namespace Framework.ResourceLocks.Storage;

internal static partial class LoggerExtensions
{
    [LoggerMessage(
        EventId = 1,
        EventName = "LockReleaseStarted",
        Level = LogLevel.Trace,
        Message = "ReleaseAsync Start: {Resource} ({LockId})"
    )]
    public static partial void LogReleaseStarted(this ILogger logger, string resource, string lockId);

    [LoggerMessage(
        EventId = 2,
        EventName = "LockReleaseReleased",
        Level = LogLevel.Debug,
        Message = "Released lock: {Resource} ({LockId})"
    )]
    public static partial void LogReleaseReleased(this ILogger logger, string resource, string lockId);

    [LoggerMessage(
        EventId = 3,
        EventName = "RenewingLock",
        Level = LogLevel.Debug,
        Message = "Renewing lock {Resource} ({LockId}) for {Duration:g}"
    )]
    public static partial void LogRenewingLock(this ILogger logger, string resource, string lockId, TimeSpan? duration);

    [LoggerMessage(
        EventId = 4,
        EventName = "SubscribingToLockReleased",
        Level = LogLevel.Trace,
        Message = "Subscribing to cache lock released"
    )]
    public static partial void LogSubscribingToLockReleased(this ILogger logger);

    [LoggerMessage(
        EventId = 5,
        EventName = "SubscribedToLockReleased",
        Level = LogLevel.Trace,
        Message = "Subscribed to cache lock released"
    )]
    public static partial void LogSubscribedToLockReleased(this ILogger logger);

    [LoggerMessage(
        EventId = 6,
        EventName = "GotLockReleasedMessage",
        Level = LogLevel.Trace,
        Message = "Got lock released message: {Resource} ({LockId})"
    )]
    public static partial void LogGotLockReleasedMessage(this ILogger logger, string resource, string lockId);

    [LoggerMessage(
        EventId = 7,
        EventName = "AttemptingToAcquireLock",
        Level = LogLevel.Debug,
        Message = "Attempting to acquire lock {Resource} ({LockId})"
    )]
    public static partial void LogAttemptingToAcquireLock(this ILogger logger, string resource, string lockId);

    [LoggerMessage(
        EventId = 8,
        EventName = "ErrorAcquiringLock",
        Level = LogLevel.Trace,
        Message = "Error acquiring lock {Resource} ({LockId}) after {Duration:g}"
    )]
    public static partial void LogErrorAcquiringLock(
        this ILogger logger,
        Exception exception,
        string resource,
        string lockId,
        TimeSpan duration
    );

    [LoggerMessage(
        EventId = 9,
        EventName = "FailedToAcquireLock",
        Level = LogLevel.Debug,
        Message = "Failed to acquire lock {Resource} ({LockId})"
    )]
    public static partial void LogFailedToAcquireLock(this ILogger logger, string resource, string lockId);

    [LoggerMessage(
        EventId = 10,
        EventName = "CancellationRequested",
        Level = LogLevel.Trace,
        Message = "Cancellation requested while acquiring lock {Resource} ({LockId})"
    )]
    public static partial void LogCancellationRequested(this ILogger logger, string resource, string lockId);

    [LoggerMessage(
        EventId = 11,
        EventName = "CancellationRequestedForLock",
        Level = LogLevel.Trace,
        Message = "Cancellation requested for lock {Resource} ({LockId}) after {Duration:g}"
    )]
    public static partial void LogCancellationRequestedAfter(
        this ILogger logger,
        string resource,
        string lockId,
        TimeSpan duration
    );

    [LoggerMessage(
        EventId = 12,
        EventName = "DelayBeforeRetry",
        Level = LogLevel.Trace,
        Message = "Will wait {Delay:g} before retrying to acquire lock {Resource} ({LockId})"
    )]
    public static partial void LogDelayBeforeRetry(this ILogger logger, TimeSpan delay, string resource, string lockId);

    [LoggerMessage(
        EventId = 13,
        EventName = "LongLockAcquired",
        Level = LogLevel.Warning,
        Message = "Acquired lock {Resource} ({LockId}) after {Duration:g}"
    )]
    public static partial void LogLongLockAcquired(
        this ILogger logger,
        string resource,
        string lockId,
        TimeSpan duration
    );

    [LoggerMessage(
        EventId = 14,
        EventName = "AcquiredLock",
        Level = LogLevel.Debug,
        Message = "Acquired lock {Resource} ({LockId}) after {Duration:g}"
    )]
    public static partial void LogAcquiredLock(this ILogger logger, string resource, string lockId, TimeSpan duration);

    [LoggerMessage(
        EventId = 15,
        EventName = "FailedToAcquireLockAfter",
        Level = LogLevel.Warning,
        Message = "Failed to acquire lock {Resource} ({LockId}) after {Duration:g}"
    )]
    public static partial void LogFailedToAcquireLockAfter(
        this ILogger logger,
        string resource,
        string lockId,
        TimeSpan duration
    );

    [LoggerMessage(
        EventId = 16,
        EventName = "ThrottlingLockTryingToAcquireLock",
        Level = LogLevel.Trace,
        Message = "Throttling lock trying to acquire lock {Resource}"
    )]
    public static partial void LogThrottlingLockTryingToAcquireLock(this ILogger logger, string resource);

    [LoggerMessage(
        EventId = 17,
        EventName = "ThrottlingLockHitCount",
        Level = LogLevel.Trace,
        Message = "Throttling lock hit count for Resource={Resource} HitCount={HitCount} max={MaxHitsPerPeriod}"
    )]
    public static partial void LogThrottlingLockHitCount(
        this ILogger logger,
        string resource,
        long? hitCount,
        long maxHitsPerPeriod
    );

    [LoggerMessage(
        EventId = 18,
        EventName = "ThrottlingLockMaxHitsExceeded",
        Level = LogLevel.Trace,
        Message = "Throttling lock max hits exceeded for {Resource}"
    )]
    public static partial void LogThrottlingLockMaxHitsExceeded(this ILogger logger, string resource);

    [LoggerMessage(
        EventId = 19,
        EventName = "ThrottlingLockErrorAcquiringLock",
        Level = LogLevel.Error,
        Message = "Throttling lock error acquiring lock Resource={Resource} Error={Error}"
    )]
    public static partial void LogThrottlingLockErrorAcquiringLock(
        this ILogger logger,
        Exception exception,
        string resource,
        string error
    );
}
