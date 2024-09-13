// Copyright (c) Mahmoud Shaheen, 2024. All rights reserved

using Framework.Kernel.BuildingBlocks.Abstractions;
using Framework.Kernel.Checks;
using Framework.ResourceLocks.Storage.RegularLocks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Framework.ResourceLocks.Storage.ThrottlingLocks;

public sealed class ThrottlingResourceLockProvider(
    IThrottlingResourceLockStorage storage,
    TimeProvider timeProvider,
    ILogger<StorageResourceLockProvider> logger,
    IOptions<ThrottlingResourceLockOptions> optionsAccessor,
    int maxHitsPerPeriod = 100,
    TimeSpan? throttlingPeriod = null
) : IResourceThrottlingLockProvider
{
    private readonly ScopedThrottlingResourceLockStorage _storage = new(storage, optionsAccessor);
    private readonly TimeSpan _throttlingPeriod = Argument.IsPositive(throttlingPeriod ?? TimeSpan.FromMinutes(15));
    private readonly int _maxHitsPerPeriod = Argument.IsPositive(maxHitsPerPeriod);

    public async Task<IResourceThrottlingLock?> TryAcquireAsync(string resource, TimeSpan? acquireTimeout = null)
    {
        Argument.IsNotNullOrWhiteSpace(resource);
        var normalizedAcquireTimeout = _NormalizeAcquireTimeout(acquireTimeout);

        logger.LogThrottlingLockTryingToAcquireLock(resource);

        using var acquireTimeoutCts = new CancellationTokenSource();

        if (normalizedAcquireTimeout is not null)
        {
            acquireTimeoutCts.CancelAfter(normalizedAcquireTimeout.Value);
        }

        var allowLock = false;
        byte errors = 0;
        var timestamp = timeProvider.GetTimestamp();

        do
        {
            var cacheKey = _GetCacheKey(resource, timeProvider.GetUtcNow().UtcDateTime);

            try
            {
                if (logger.IsEnabled(LogLevel.Trace))
                {
                    var now = timeProvider.GetUtcNow();
                    var currentTime = now.ToString("mm:ss.fff", CultureInfo.InvariantCulture);

                    var throttlingPeriodText = _GetDateCurrentThrottlingPeriodStarted(now.UtcDateTime)
                        .ToString("mm:ss.fff", CultureInfo.InvariantCulture);

                    logger.LogTrace(
                        "Current time: {CurrentTime} throttle: {ThrottlingPeriod} key: {Key}",
                        currentTime,
                        throttlingPeriodText,
                        cacheKey
                    );
                }

                var hitCount = await _storage.GetAsync<long?>(cacheKey, defaultValue: 0).AnyContext();
                logger.LogThrottlingLockHitCount(resource, hitCount, _maxHitsPerPeriod);

                if (hitCount <= _maxHitsPerPeriod - 1)
                {
                    var expiration = _Ceiling(timeProvider.GetUtcNow().UtcDateTime, _throttlingPeriod)
                        .Subtract(timeProvider.GetUtcNow().UtcDateTime);

                    hitCount = await storage.IncrementAsync(cacheKey, 1, expiration).AnyContext();

                    // Make sure someone didn't beat us to it.
                    if (hitCount <= _maxHitsPerPeriod)
                    {
                        allowLock = true;
                        break;
                    }

                    logger.LogTrace("Max hits exceeded after increment for {Resource}", resource);
                }
                else
                {
                    logger.LogThrottlingLockMaxHitsExceeded(resource);
                }

                if (acquireTimeoutCts.IsCancellationRequested)
                {
                    break;
                }

                var sleepUntil = _Ceiling(timeProvider.GetUtcNow().UtcDateTime, _throttlingPeriod).AddMilliseconds(1);
                if (sleepUntil > timeProvider.GetUtcNow().UtcDateTime)
                {
                    logger.LogTrace(
                        "Sleeping until key expires: {SleepUntil}",
                        sleepUntil - timeProvider.GetUtcNow().UtcDateTime
                    );

                    try
                    {
                        await timeProvider
                            .Delay(sleepUntil - timeProvider.GetUtcNow().UtcDateTime, acquireTimeoutCts.Token)
                            .AnyContext();
                    }
                    catch (OperationCanceledException) { }
                }
                else
                {
                    logger.LogTrace("Default sleep");

                    try
                    {
                        await timeProvider.Delay(TimeSpan.FromMilliseconds(50), acquireTimeoutCts.Token).AnyContext();
                    }
                    catch (OperationCanceledException) { }
                }
            }
            catch (OperationCanceledException)
            {
                return null;
            }
            catch (Exception ex)
            {
                logger.LogError(
                    ex,
                    "Error acquiring throttled lock: name={Resource} message={Message}",
                    resource,
                    ex.Message
                );
                errors++;
                if (errors >= 3)
                {
                    break;
                }

                try
                {
                    await timeProvider.Delay(TimeSpan.FromMilliseconds(50), acquireTimeoutCts.Token).AnyContext();
                }
                catch (OperationCanceledException) { }
            }
        } while (!acquireTimeoutCts.Token.IsCancellationRequested);

        if (acquireTimeoutCts.Token.IsCancellationRequested)
        {
            logger.LogTrace("Cancellation requested");
        }

        if (!allowLock)
        {
            return null;
        }

        logger.LogTrace("Allowing lock: {Resource}", resource);
        var timeWaitedForLock = timeProvider.GetElapsedTime(timestamp);

        return new ResourceThrottlingLock(resource, timeWaitedForLock, timeProvider.GetUtcNow());
    }

    public async Task<bool> IsLockedAsync(string resource)
    {
        var cacheKey = _GetCacheKey(resource, timeProvider.GetUtcNow().UtcDateTime);
        var hitCount = await storage.GetAsync<long>(cacheKey, 0).AnyContext();

        return hitCount >= _maxHitsPerPeriod;
    }

    private string _GetCacheKey(string resource, DateTime now)
    {
        FormattableString format = $"{resource}:{_GetDateCurrentThrottlingPeriodStarted(now).Ticks}";

        return format.ToString(CultureInfo.InvariantCulture);
    }

    private DateTime _GetDateCurrentThrottlingPeriodStarted(DateTime now)
    {
        var elapsedTicks = now.Ticks % _throttlingPeriod.Ticks;

        return now.AddTicks(-elapsedTicks);
    }

    private static TimeSpan? _NormalizeAcquireTimeout(TimeSpan? acquireTimeout)
    {
        return acquireTimeout is null
            ? TimeSpan.FromSeconds(30)
            : acquireTimeout == Timeout.InfiniteTimeSpan
                ? null
                : Argument.IsPositive(acquireTimeout.Value);
    }

    private static DateTime _Ceiling(DateTime date, TimeSpan interval)
    {
        return date.AddTicks(interval.Ticks - (date.Ticks % interval.Ticks));
    }
}
