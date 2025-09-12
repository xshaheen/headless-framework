// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Framework.Abstractions;
using Framework.Checks;
using Framework.ResourceLocks.ThrottlingLocks;
using Humanizer;
using Microsoft.Extensions.Logging;

#pragma warning disable IDE0130
// ReSharper disable once CheckNamespace
namespace Framework.ResourceLocks;

[PublicAPI]
public sealed class ThrottlingResourceLockProvider(
    IThrottlingResourceLockStorage storage,
    ThrottlingResourceLockOptions options,
    TimeProvider timeProvider,
    ILogger<ThrottlingResourceLockProvider> logger
) : IThrottlingResourceLockProvider, IHaveTimeProvider, IHaveLogger
{
    public TimeSpan DefaultAcquireTimeout => TimeSpan.FromSeconds(30);

    TimeProvider IHaveTimeProvider.TimeProvider => timeProvider;

    ILogger IHaveLogger.Logger => logger;

    #region Acquire

    public async Task<IResourceThrottlingLock?> TryAcquireAsync(
        string resource,
        TimeSpan? acquireTimeout = null,
        CancellationToken cancellationToken = default
    )
    {
        Argument.IsNotNullOrWhiteSpace(resource);

        logger.LogThrottlingLockTryingToAcquireLock(resource);

        acquireTimeout ??= DefaultAcquireTimeout;
        var timestamp = timeProvider.GetTimestamp();
        var shouldWait = !cancellationToken.IsCancellationRequested;
        using var cts = acquireTimeout.Value.ToCancellationTokenSource(cancellationToken);

        var allowLock = false;
        byte errors = 0;
        do
        {
            var cacheKey = _GetCacheKey(resource);

            try
            {
                logger.LogThrottlingInfo(_Now(), _DatePeriodStarted(), cacheKey);

                var hitCount = await storage.GetHitCountsAsync(cacheKey).AnyContext();

                logger.LogThrottlingHitCount(resource, hitCount, options.MaxHitsPerPeriod);

                if (hitCount <= options.MaxHitsPerPeriod - 1)
                {
                    var ttl = _DatePeriodEnded().Subtract(_Now());

                    hitCount = await storage.IncrementAsync(cacheKey, ttl).AnyContext();

                    // Make sure someone didn't beat us to it.
                    if (hitCount <= options.MaxHitsPerPeriod)
                    {
                        allowLock = true;

                        break;
                    }

                    logger.LogThrottlingMaxHitsExceededAfterCurrent(resource);
                }
                else
                {
                    logger.LogThrottlingMaxHitsExceeded(resource);
                }

                if (cts.IsCancellationRequested)
                {
                    break;
                }

                // Sleep until the end of the current throttling period or the acquire timeout.
                var sleepUntil = _DatePeriodEnded().AddMilliseconds(1);

                if (sleepUntil > _Now())
                {
                    logger.LogThrottlingSleepUntil(resource, sleepUntil - _Now());
                    await timeProvider.DelayUntilElapsedOrCancel(sleepUntil - _Now(), cts.Token).AnyContext();
                }
                else
                {
                    logger.LogThrottlingDefaultSleep(resource);
                    await timeProvider.DelayUntilElapsedOrCancel(50.Milliseconds(), cts.Token).AnyContext();
                }
            }
            catch (OperationCanceledException)
            {
                return null;
            }
            catch (Exception e)
            {
                logger.LogThrottlingError(e, resource, e.Message);
                errors++;

                if (errors >= 3)
                {
                    break;
                }

                await timeProvider.DelayUntilElapsedOrCancel(50.Milliseconds(), cts.Token).AnyContext();
            }
        } while (!cts.IsCancellationRequested);

        var elapsed = timeProvider.GetElapsedTime(timestamp);

        if (!allowLock)
        {
            if (shouldWait && cancellationToken.IsCancellationRequested)
            {
                logger.LogThrottlingCancelled(resource, elapsed);
            }
            else
            {
                logger.LogThrottlingTimeout(resource, elapsed);
            }

            return null;
        }

        logger.LogThrottlingAcquired(resource, elapsed);

        return new ResourceThrottlingLock(resource, elapsed, timeProvider.GetUtcNow());
    }

    #endregion

    #region IsLocked

    public async Task<bool> IsLockedAsync(string resource)
    {
        Argument.IsNotNull(resource);

        var cacheKey = _GetCacheKey(resource);
        var hitCount = await storage.GetHitCountsAsync(cacheKey).AnyContext();

        return hitCount >= options.MaxHitsPerPeriod;
    }

    #endregion

    #region Helpers

    private string _GetCacheKey(string resource)
    {
        FormattableString format = $"{options.KeyPrefix}{resource}:{_DatePeriodStarted().Ticks}";

        return format.ToString(CultureInfo.InvariantCulture);
    }

    private DateTime _Now() => timeProvider.GetUtcNow().UtcDateTime;

    private DateTime _DatePeriodStarted() => _Now().Floor(options.ThrottlingPeriod);

    private DateTime _DatePeriodEnded() => _Now().Ceiling(options.ThrottlingPeriod);

    #endregion
}
