// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Microsoft.Extensions.Logging;

#pragma warning disable IDE0130 // ReSharper disable once CheckNamespace
namespace Headless.DistributedLocks;

/// <summary>
/// High-performance <see cref="LoggerMessage"/>-based log methods for the distributed-lock and
/// distributed-semaphore providers. Non-generated helpers that compute elapsed time from a
/// <see cref="TimeProvider"/> timestamp are defined as regular methods to avoid allocating a
/// <see cref="TimeSpan"/> before the log-level guard.
/// </summary>
public static partial class RegularLockLoggerExtensions
{
    /// <summary>
    /// Logs a transient storage error during an acquire attempt, computing elapsed time from the
    /// provided <paramref name="timeProvider"/> and <paramref name="timestamp"/> only when
    /// <see cref="LogLevel.Trace"/> is enabled (avoids the elapsed-time computation on the hot path).
    /// </summary>
    /// <param name="logger">The logger instance.</param>
    /// <param name="exception">The exception that occurred.</param>
    /// <param name="resource">The resource being acquired.</param>
    /// <param name="leaseId">The lease ID of the acquire attempt.</param>
    /// <param name="timeProvider">Time provider used to compute elapsed time from <paramref name="timestamp"/>.</param>
    /// <param name="timestamp">The <see cref="TimeProvider.GetTimestamp"/> value captured at the start of the acquire.</param>
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

    [LoggerMessage(
        EventId = 24,
        EventName = "TryOnceSafetyDeadlineFired",
        Level = LogLevel.Warning,
        Message = "Non-blocking lock acquire hit its safety deadline after {Duration:g} (lock-store stalled, not contention): R={Resource} Id={LeaseId}"
    )]
    public static partial void LogTryOnceSafetyDeadlineFired(
        this ILogger logger,
        string resource,
        string leaseId,
        TimeSpan duration
    );
}
