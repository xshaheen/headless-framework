// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Framework.Kernel.Checks;
using Jitbit.Utils;
using Microsoft.Extensions.Logging;

namespace Framework.ResourceLocks.Local;

[PublicAPI]
public sealed class LocalResourceThrottlingLockProvider(
    TimeProvider timeProvider,
    ThrottlingResourceLockOptions options,
    ILogger<LocalResourceThrottlingLockProvider> logger
) : IResourceThrottlingLockProvider, IDisposable
{
    private readonly FastCache<string, ResourceLock> _resources = new();

    public async Task<IResourceThrottlingLock?> TryAcquireAsync(string resource, TimeSpan? acquireTimeout = null)
    {
        acquireTimeout ??= TimeSpan.FromSeconds(30);
        Argument.IsNotNullOrWhiteSpace(resource);
        logger.LogThrottlingLockTryingToAcquireLock(resource);

        using var acquireTimeoutCts = new CancellationTokenSource();
        if (acquireTimeout != Timeout.InfiniteTimeSpan)
        {
            acquireTimeoutCts.CancelAfter(acquireTimeout.Value);
        }

        var allowLock = false;
        byte errors = 0;
        var timestamp = timeProvider.GetTimestamp();

        do
        {
            var cacheKey = _GetCacheKey(resource);

            try
            {
                if (logger.IsEnabled(LogLevel.Trace))
                {
                    var throttlingPeriodText = _GetDateCurrentThrottlingPeriodStarted()
                        .ToString("mm:ss.fff", CultureInfo.InvariantCulture);

                    logger.LogTrace(
                        "Current time: {CurrentTime} throttle: {ThrottlingPeriod} key: {Key}",
                        timeProvider.GetUtcNow().ToString("mm:ss.fff", CultureInfo.InvariantCulture),
                        throttlingPeriodText,
                        cacheKey
                    );
                }

                var hitCount = _GetHitsCount(resource);
                logger.LogThrottlingLockHitCount(resource, hitCount, options.MaxHitsPerPeriod);

                if (hitCount <= options.MaxHitsPerPeriod - 1)
                {
                    var expiration = _GetDateCurrentThrottlingPeriodEnded()
                        .Subtract(timeProvider.GetUtcNow().UtcDateTime);

                    hitCount = _Increment(cacheKey, expiration);

                    // Make sure someone didn't beat us to it.
                    if (hitCount <= options.MaxHitsPerPeriod)
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

                var sleepUntil = _GetDateCurrentThrottlingPeriodEnded().AddMilliseconds(1);

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
        } while (!acquireTimeoutCts.IsCancellationRequested);

        if (acquireTimeoutCts.IsCancellationRequested)
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

    public Task<bool> IsLockedAsync(string resource)
    {
        Argument.IsNotNullOrWhiteSpace(resource);

        return Task.FromResult(_GetHitsCount(resource) >= options.MaxHitsPerPeriod);
    }

    public void Dispose()
    {
        _resources.Dispose();
    }

    #region Helpers

    private string _GetCacheKey(string resource)
    {
        FormattableString format = $"{resource}:{_GetDateCurrentThrottlingPeriodStarted().Ticks}";

        return format.ToString(CultureInfo.InvariantCulture);
    }

    private DateTime _GetDateCurrentThrottlingPeriodStarted()
    {
        var now = timeProvider.GetUtcNow().UtcDateTime;
        var elapsedTicks = now.Ticks % options.ThrottlingPeriod.Ticks;

        return now.AddTicks(-elapsedTicks);
    }

    private DateTime _GetDateCurrentThrottlingPeriodEnded()
    {
        var now = timeProvider.GetUtcNow().UtcDateTime;
        var elapsedTicks = now.Ticks % options.ThrottlingPeriod.Ticks;

        return now.AddTicks(options.ThrottlingPeriod.Ticks - elapsedTicks);
    }

    private int _GetHitsCount(string resource)
    {
        var normalizeResource = options.KeyPrefix + resource;

        return _resources.TryGet(normalizeResource, out var resourceLock) ? resourceLock.HitsCount : 0;
    }

    private int _Increment(string resource, TimeSpan expiration)
    {
        var normalizeResource = options.KeyPrefix + resource;

        var resourceLock = _resources.GetOrAdd(
            key: normalizeResource,
            valueFactory: static (_, maxHits) => new ResourceLock(maxHits),
            ttl: expiration,
            factoryArgument: options.MaxHitsPerPeriod
        );

        resourceLock.Hit();

        return resourceLock.HitsCount;
    }

    private sealed record ResourceLock(int MaxHits)
    {
        public int HitsCount { get; private set; }

        public bool IsLocked { get; private set; }

        public void Hit()
        {
            if (IsLocked)
            {
                return;
            }

            HitsCount++;
            IsLocked = HitsCount > MaxHits;
        }
    }

    #endregion
}
