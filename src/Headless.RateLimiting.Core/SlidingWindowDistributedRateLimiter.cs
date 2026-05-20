// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Abstractions;
using Headless.Checks;
using Humanizer;
using Microsoft.Extensions.Logging;

#pragma warning disable IDE0130
// ReSharper disable once CheckNamespace
namespace Headless.RateLimiting;

internal sealed class SlidingWindowDistributedRateLimiter(
    IDistributedRateLimiterStorage storage,
    SlidingWindowRateLimiterOptions options,
    TimeProvider timeProvider,
    ILogger<SlidingWindowDistributedRateLimiter> logger
) : IDistributedRateLimiter, IHaveTimeProvider, IHaveLogger
{
    public TimeSpan DefaultAcquireTimeout => TimeSpan.FromSeconds(30);

    TimeProvider IHaveTimeProvider.TimeProvider => timeProvider;

    ILogger IHaveLogger.Logger => logger;

    #region Acquire

    public async Task<IDistributedRateLimiterLease?> TryAcquireAsync(
        string resource,
        TimeSpan? acquireTimeout = null,
        CancellationToken cancellationToken = default
    )
    {
        Argument.IsNotNullOrWhiteSpace(resource);

        logger.LogRateLimiterTryingToAcquireLease(resource);

        acquireTimeout = _NormalizeAcquireTimeout(acquireTimeout);
        var timestamp = timeProvider.GetTimestamp();

        // Use the injected TimeProvider so FakeTimeProvider-driven tests are deterministic.
        // Mirrors DistributedLockProvider's acquire-timeout CTS construction.
        using var timeoutCts =
            acquireTimeout == TimeSpan.Zero ? null : timeProvider.CreateCancellationTokenSource(acquireTimeout.Value);
        using var linkedCts = timeoutCts is null
            ? null
            : CancellationTokenSource.CreateLinkedTokenSource(timeoutCts.Token, cancellationToken);
        var acquireToken = linkedCts?.Token ?? cancellationToken;

        var allowLease = false;
        byte errors = 0;
        do
        {
            var now = _Now();
            var periodStarted = _DatePeriodStarted(now);
            var periodEnded = _DatePeriodEnded(now);
            var cacheKey = _GetCacheKey(resource, periodStarted);

            try
            {
                if (logger.IsEnabled(LogLevel.Trace))
                {
                    logger.LogRateLimiterInfo(now, periodStarted, cacheKey);
                }

                var hitCount = await storage.GetHitCountsAsync(cacheKey, acquireToken).ConfigureAwait(false);

                logger.LogRateLimiterHitCount(resource, hitCount, options.MaxHitsPerPeriod);

                if (hitCount <= options.MaxHitsPerPeriod - 1)
                {
                    var beforeIncrement = _Now();
                    if (_DatePeriodStarted(beforeIncrement) != periodStarted)
                    {
                        continue;
                    }

                    var ttl = periodEnded.Subtract(beforeIncrement);
                    if (ttl <= TimeSpan.Zero)
                    {
                        continue;
                    }

                    hitCount = await storage.IncrementAsync(cacheKey, ttl, acquireToken).ConfigureAwait(false);

                    // Make sure someone didn't beat us to it.
                    if (hitCount <= options.MaxHitsPerPeriod)
                    {
                        allowLease = true;

                        break;
                    }

                    logger.LogRateLimiterMaxHitsExceededAfterCurrent(resource);
                }
                else
                {
                    logger.LogRateLimiterMaxHitsExceeded(resource);
                }

                if (acquireToken.IsCancellationRequested || acquireTimeout == TimeSpan.Zero)
                {
                    break;
                }

                // Sleep until the current rate-limiting period rotates or the acquire timeout expires.
                var sleepUntil = periodEnded.AddMilliseconds(1);
                var beforeSleep = _Now();

                if (sleepUntil > beforeSleep)
                {
                    var delay = sleepUntil - beforeSleep;
                    if (logger.IsEnabled(LogLevel.Debug))
                    {
                        logger.LogRateLimiterSleepUntil(resource, delay);
                    }

                    await timeProvider.DelayUntilElapsedOrCancel(delay, acquireToken).ConfigureAwait(false);
                    await _WaitUntilPeriodRotatesAsync(resource, periodStarted, acquireToken).ConfigureAwait(false);
                }
                else
                {
                    logger.LogRateLimiterDefaultSleep(resource);
                    await timeProvider.DelayUntilElapsedOrCancel(50.Milliseconds(), acquireToken).ConfigureAwait(false);
                    await _WaitUntilPeriodRotatesAsync(resource, periodStarted, acquireToken).ConfigureAwait(false);
                }
            }
            catch (OperationCanceledException)
            {
                return null;
            }
            catch (Exception e)
            {
                logger.LogRateLimiterError(e, resource, e.Message);
                errors++;

                if (errors >= 3 || acquireTimeout == TimeSpan.Zero)
                {
                    break;
                }

                await timeProvider.DelayUntilElapsedOrCancel(50.Milliseconds(), acquireToken).ConfigureAwait(false);
            }
        } while (!acquireToken.IsCancellationRequested);

        var elapsed = timeProvider.GetElapsedTime(timestamp);

        if (!allowLease)
        {
            // Route purely on the caller's cancellation token so that callers who cancel mid-wait
            // see a Cancelled log, while timeouts (only the linked CTS firing) see a Timeout log.
            if (cancellationToken.IsCancellationRequested)
            {
                logger.LogRateLimiterCancelled(resource, elapsed);
            }
            else
            {
                logger.LogRateLimiterTimeout(resource, elapsed);
            }

            return null;
        }

        logger.LogRateLimiterLeaseAcquired(resource, elapsed);

        return new DistributedRateLimiterLease(resource, elapsed, timeProvider.GetUtcNow());
    }

    #endregion

    #region IsLocked

    public async Task<bool> IsLockedAsync(string resource, CancellationToken cancellationToken = default)
    {
        Argument.IsNotNull(resource);

        var cacheKey = _GetCacheKey(resource);
        var hitCount = await storage.GetHitCountsAsync(cacheKey, cancellationToken).ConfigureAwait(false);

        return hitCount >= options.MaxHitsPerPeriod;
    }

    #endregion

    #region Helpers

    private string _GetCacheKey(string resource, DateTime periodStarted)
    {
        return string.Concat(
            options.KeyPrefix,
            resource,
            ":",
            periodStarted.Ticks.ToString(CultureInfo.InvariantCulture)
        );
    }

    private string _GetCacheKey(string resource) => _GetCacheKey(resource, _DatePeriodStarted(_Now()));

    private DateTime _Now() => timeProvider.GetUtcNow().UtcDateTime;

    private DateTime _DatePeriodStarted(DateTime now) => now.Floor(options.RateLimitingPeriod);

    private DateTime _DatePeriodEnded(DateTime now) => now.Ceiling(options.RateLimitingPeriod);

    private TimeSpan _NormalizeAcquireTimeout(TimeSpan? acquireTimeout)
    {
        // Allow null (use default) and Timeout.InfiniteTimeSpan as the explicit "wait forever" sentinel;
        // reject other negatives. Mirrors DistributedLockProvider._ValidateAcquireTimeout.
        if (acquireTimeout is null)
        {
            return DefaultAcquireTimeout;
        }

        if (acquireTimeout == Timeout.InfiniteTimeSpan)
        {
            return Timeout.InfiniteTimeSpan;
        }

        Argument.IsPositiveOrZero(acquireTimeout.Value, paramName: nameof(acquireTimeout));

        return acquireTimeout.Value;
    }

    private async Task _WaitUntilPeriodRotatesAsync(
        string resource,
        DateTime previousPeriodStarted,
        CancellationToken cancellationToken
    )
    {
        for (var spinIteration = 0; spinIteration < 100; spinIteration++)
        {
            if (_DatePeriodStarted(_Now()) != previousPeriodStarted)
            {
                return;
            }

            if (cancellationToken.IsCancellationRequested)
            {
                return;
            }

            await timeProvider.DelayUntilElapsedOrCancel(1.Milliseconds(), cancellationToken).ConfigureAwait(false);
        }

        logger.LogRateLimiterClockFrozen(resource);
    }

    #endregion
}

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
    public static partial void LogRateLimiterError(
        this ILogger logger,
        Exception exception,
        string resource,
        string message
    );

    [LoggerMessage(
        EventId = 12,
        EventName = "RateLimiterClockFrozen",
        Level = LogLevel.Warning,
        Message = "Rate-limiting period did not rotate after spin cap for {Resource}"
    )]
    public static partial void LogRateLimiterClockFrozen(this ILogger logger, string resource);
}
