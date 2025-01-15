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
public interface IThrottlingResourceLockProvider
{
    public TimeSpan DefaultAcquireTimeout { get; }

    /// <summary>
    /// Acquires a resource lock for a specified resource this method will block
    /// until the lock is acquired or the <paramref name="acquireTimeout"/> is reached.
    /// </summary>
    /// <param name="resource">The resource to acquire the lock for.</param>
    /// <param name="acquireTimeout">
    /// The amount of time to wait for the lock to be acquired. The allowed values are:
    /// <list type="bullet">
    /// <item><see langword="null"/>: means the default value (1 minute).</item>
    /// <item><see cref="Timeout.InfiniteTimeSpan"/> (-1 milliseconds): means infinity wait to acquire</item>
    /// <item>Value greater than 0.</item>
    /// </list>
    /// </param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>
    /// A task that represents the asynchronous operation.
    /// The task result contains the acquired lock or null if the lock could not be acquired.
    /// </returns>
    Task<IResourceThrottlingLock?> TryAcquireAsync(
        string resource,
        TimeSpan? acquireTimeout = null,
        CancellationToken cancellationToken = default
    );

    /// <summary>
    /// Checks if a specified resource is currently locked.
    /// </summary>
    Task<bool> IsLockedAsync(string resource);
}

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
                    await timeProvider.SafeDelay(sleepUntil - _Now(), cts.Token).AnyContext();
                }
                else
                {
                    logger.LogThrottlingDefaultSleep(resource);
                    await timeProvider.SafeDelay(50.Milliseconds(), cts.Token).AnyContext();
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

                await timeProvider.SafeDelay(50.Milliseconds(), cts.Token).AnyContext();
            }
        } while (!cts.IsCancellationRequested);

        var elapsed = timeProvider.GetElapsedTime(timestamp);

        if (cts.IsCancellationRequested)
        {
            logger.LogThrottlingFailed(resource, elapsed);
        }

        if (!allowLock)
        {
            return null;
        }

        logger.LogThrottlingAcquired(resource, elapsed);

        return new ResourceThrottlingLock(resource, elapsed, timeProvider.GetUtcNow());
    }

    public async Task<bool> IsLockedAsync(string resource)
    {
        Argument.IsNotNull(resource);

        var cacheKey = _GetCacheKey(resource);
        var hitCount = await storage.GetHitCountsAsync(cacheKey).AnyContext();

        return hitCount >= options.MaxHitsPerPeriod;
    }

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
