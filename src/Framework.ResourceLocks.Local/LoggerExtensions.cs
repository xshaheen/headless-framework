// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Microsoft.Extensions.Logging;

namespace Framework.ResourceLocks.Local;

internal static partial class LoggerExtensions
{
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
}
