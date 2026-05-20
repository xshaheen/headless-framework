// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Microsoft.Extensions.Logging;

#pragma warning disable IDE0130
// ReSharper disable once CheckNamespace
namespace Headless.RateLimiting;

internal static partial class RateLimiterLoggerExtensions
{
    [LoggerMessage(
        EventId = 1,
        EventName = "RateLimiterTryingToAcquireLease",
        Level = LogLevel.Trace,
        Message = "Trying to acquire rate-limiter lease for {Resource}"
    )]
    public static partial void LogRateLimiterTryingToAcquireLease(this ILogger logger, string resource);

    [LoggerMessage(
        EventId = 2,
        EventName = "RateLimiterInfo",
        Level = LogLevel.Trace,
        Message = "Current time: {CurrentTime:mm:ss.fff} rate-limiting period: {RateLimitingPeriod:mm:ss.fff} key: {Key}"
    )]
    public static partial void LogRateLimiterInfo(
        this ILogger logger,
        DateTime currentTime,
        DateTime rateLimitingPeriod,
        string key
    );

    [LoggerMessage(
        EventId = 3,
        EventName = "RateLimiterHitCount",
        Level = LogLevel.Trace,
        Message = "Hit count for Resource={Resource} HitCount={HitCount} max={MaxHitsPerPeriod}"
    )]
    public static partial void LogRateLimiterHitCount(
        this ILogger logger,
        string resource,
        long? hitCount,
        long maxHitsPerPeriod
    );

    [LoggerMessage(
        EventId = 4,
        EventName = "RateLimiterMaxHitsExceeded",
        Level = LogLevel.Trace,
        Message = "Max hits exceeded for {Resource}"
    )]
    public static partial void LogRateLimiterMaxHitsExceeded(this ILogger logger, string resource);

    [LoggerMessage(
        EventId = 5,
        EventName = "RateLimiterMaxHitsExceededAfterCurrent",
        Level = LogLevel.Trace,
        Message = "Max hits exceeded after increment for {Resource}"
    )]
    public static partial void LogRateLimiterMaxHitsExceededAfterCurrent(this ILogger logger, string resource);

    [LoggerMessage(
        EventId = 6,
        EventName = "RateLimiterDefaultSleep",
        Level = LogLevel.Trace,
        Message = "Sleeping for default time for {Resource}"
    )]
    public static partial void LogRateLimiterDefaultSleep(this ILogger logger, string resource);

    [LoggerMessage(
        EventId = 7,
        EventName = "RateLimiterSleepUntil",
        Level = LogLevel.Trace,
        Message = "Sleeping until key expires for {Resource}: {SleepTime}"
    )]
    public static partial void LogRateLimiterSleepUntil(this ILogger logger, string resource, TimeSpan sleepTime);

    [LoggerMessage(
        EventId = 8,
        EventName = "RateLimiterTimeout",
        Level = LogLevel.Trace,
        Message = "Timeout for {Resource} after {Elapsed}"
    )]
    public static partial void LogRateLimiterTimeout(this ILogger logger, string resource, TimeSpan elapsed);

    [LoggerMessage(
        EventId = 9,
        EventName = "RateLimiterCancelled",
        Level = LogLevel.Trace,
        Message = "Cancellation requested for {Resource} after {Elapsed}"
    )]
    public static partial void LogRateLimiterCancelled(this ILogger logger, string resource, TimeSpan elapsed);

    [LoggerMessage(
        EventId = 10,
        EventName = "RateLimiterLeaseAcquired",
        Level = LogLevel.Trace,
        Message = "Lease allowed for {Resource} in {Elapsed}"
    )]
    public static partial void LogRateLimiterLeaseAcquired(this ILogger logger, string resource, TimeSpan elapsed);

    [LoggerMessage(
        EventId = 11,
        EventName = "RateLimiterError",
        Level = LogLevel.Error,
        Message = "Error acquiring rate-limiter lease ({Resource}): {Message}"
    )]
    public static partial void LogRateLimiterError(this ILogger logger, Exception e, string resource, string message);

    [LoggerMessage(
        EventId = 12,
        EventName = "RateLimiterClockFrozen",
        Level = LogLevel.Warning,
        Message = "Rate-limiting period did not rotate after spin cap for {Resource}"
    )]
    public static partial void LogRateLimiterClockFrozen(this ILogger logger, string resource);
}
