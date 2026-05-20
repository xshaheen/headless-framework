// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Abstractions;
using Headless.Checks;
using Humanizer;
using Microsoft.Extensions.Logging;

#pragma warning disable IDE0130
// ReSharper disable once CheckNamespace
namespace Headless.RateLimiting;

[PublicAPI]
public sealed class SlidingWindowDistributedRateLimiter(
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
        var shouldWait = !cancellationToken.IsCancellationRequested;
        using CancellationTokenSource? acquireTimeoutCts =
            acquireTimeout == TimeSpan.Zero ? null : acquireTimeout.Value.ToCancellationTokenSource(cancellationToken);
        var acquireToken = acquireTimeoutCts?.Token ?? cancellationToken;

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
            if (shouldWait && cancellationToken.IsCancellationRequested)
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
        var timeout = acquireTimeout ?? DefaultAcquireTimeout;

        if (timeout < TimeSpan.Zero && timeout != Timeout.InfiniteTimeSpan)
        {
            throw new ArgumentOutOfRangeException(
                nameof(acquireTimeout),
                acquireTimeout,
                "Acquire timeout must be null, Timeout.InfiniteTimeSpan, TimeSpan.Zero, or a positive value."
            );
        }

        return timeout;
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
