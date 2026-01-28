// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Microsoft.Extensions.Logging;

#pragma warning disable IDE0130
// ReSharper disable once CheckNamespace
namespace Headless.DistributedLocks;

internal static partial class ThrottlingLockLoggerExtensions
{
    [LoggerMessage(
        EventId = 15,
        EventName = "ThrottlingLockTryingToAcquireLock",
        Level = LogLevel.Trace,
        Message = "Trying to acquire lock {Resource}"
    )]
    public static partial void LogThrottlingLockTryingToAcquireLock(this ILogger logger, string resource);

    [LoggerMessage(
        EventId = 16,
        EventName = "LogThrottlingInfo",
        Level = LogLevel.Trace,
        Message = "Current time: {CurrentTime:mm:ss.fff} throttle: {ThrottlingPeriod:mm:ss.fff} key: {Key}"
    )]
    public static partial void LogThrottlingInfo(
        this ILogger logger,
        DateTime currentTime,
        DateTime throttlingPeriod,
        string key
    );

    [LoggerMessage(
        EventId = 17,
        EventName = "ThrottlingLockHitCount",
        Level = LogLevel.Trace,
        Message = "Hit count for Resource={Resource} HitCount={HitCount} max={MaxHitsPerPeriod}"
    )]
    public static partial void LogThrottlingHitCount(
        this ILogger logger,
        string resource,
        long? hitCount,
        long maxHitsPerPeriod
    );

    [LoggerMessage(
        EventId = 18,
        EventName = "ThrottlingLockMaxHitsExceeded",
        Level = LogLevel.Trace,
        Message = "Max hits exceeded for {Resource}"
    )]
    public static partial void LogThrottlingMaxHitsExceeded(this ILogger logger, string resource);

    [LoggerMessage(
        EventId = 19,
        EventName = "ThrottlingLockMaxHitsExceededAfterCurrent",
        Level = LogLevel.Trace,
        Message = "Max hits exceeded after increment for {Resource}"
    )]
    public static partial void LogThrottlingMaxHitsExceededAfterCurrent(this ILogger logger, string resource);

    [LoggerMessage(
        EventId = 20,
        EventName = "ThrottlingDefaultSleep",
        Level = LogLevel.Trace,
        Message = "Sleeping for default time for {Resource}"
    )]
    public static partial void LogThrottlingDefaultSleep(this ILogger logger, string resource);

    [LoggerMessage(
        EventId = 21,
        EventName = "ThrottlingSleepUntil",
        Level = LogLevel.Trace,
        Message = "Sleeping until key expires for {Resource}: {SleepTime}"
    )]
    public static partial void LogThrottlingSleepUntil(this ILogger logger, string resource, TimeSpan sleepTime);

    [LoggerMessage(
        EventId = 22,
        EventName = "ThrottlingTimeout",
        Level = LogLevel.Trace,
        Message = "Timeout for {Resource} after {Elapsed}"
    )]
    public static partial void LogThrottlingTimeout(this ILogger logger, string resource, TimeSpan elapsed);

    [LoggerMessage(
        EventId = 23,
        EventName = "ThrottlingCancelled",
        Level = LogLevel.Trace,
        Message = "Cancellation requested for {Resource} after {Elapsed}"
    )]
    public static partial void LogThrottlingCancelled(this ILogger logger, string resource, TimeSpan elapsed);

    [LoggerMessage(
        EventId = 24,
        EventName = "ThrottlingAcquired",
        Level = LogLevel.Trace,
        Message = "Lock allowed for {Resource} in {Elapsed}"
    )]
    public static partial void LogThrottlingAcquired(this ILogger logger, string resource, TimeSpan elapsed);

    [LoggerMessage(
        EventId = 25,
        EventName = "ThrottlingError",
        Level = LogLevel.Error,
        Message = "Error acquiring throttled lock ({Resource}): {Message}"
    )]
    public static partial void LogThrottlingError(this ILogger logger, Exception e, string resource, string message);
}
