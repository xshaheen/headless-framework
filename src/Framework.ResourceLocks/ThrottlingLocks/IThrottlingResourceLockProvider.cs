// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Framework.Checks;
using Framework.ResourceLocks.ThrottlingLocks;
using Microsoft.Extensions.Logging;

#pragma warning disable IDE0130
// ReSharper disable once CheckNamespace
namespace Framework.ResourceLocks;

[PublicAPI]
public interface IThrottlingResourceLockProvider : IAsyncDisposable
{
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
) : IThrottlingResourceLockProvider
{
    public static TimeSpan DefaultAcquireTimeout => TimeSpan.FromSeconds(30);

    public async Task<IResourceThrottlingLock?> TryAcquireAsync(
        string resource,
        TimeSpan? acquireTimeout = null,
        CancellationToken cancellationToken = default
    )
    {
        Argument.IsNotNullOrWhiteSpace(resource);
        logger.LogThrottlingLockTryingToAcquireLock(resource);

        var timestamp = timeProvider.GetTimestamp();
        var allowLock = false;
        byte errors = 0;
        using var acquireTimeoutCts = _GetAcquireCancellation(acquireTimeout, cancellationToken);

        do
        {
            var cacheKey = _GetKey(resource);

            try
            {
                _LogCurrentInformation(cacheKey);
                var hitCount = await storage.GetHitCountsAsync(cacheKey);
                logger.LogThrottlingLockHitCount(resource, hitCount, options.MaxHitsPerPeriod);

                if (hitCount <= options.MaxHitsPerPeriod - 1)
                {
                    var ttl = _GetDateCurrentThrottlingPeriodEnded().Subtract(_GetUtcDateTime());

                    hitCount = await storage.IncrementAsync(cacheKey, ttl);

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

                // Sleep until the end of the current throttling period or the acquire timeout.
                var datePeriodEnd = _GetDateCurrentThrottlingPeriodEnded().AddMilliseconds(1);

                if (datePeriodEnd > _GetUtcDateTime())
                {
                    logger.LogTrace("Sleeping until key expires: {SleepUntil}", datePeriodEnd - _GetUtcDateTime());
                    await timeProvider.Delay(datePeriodEnd - _GetUtcDateTime(), acquireTimeoutCts.Token).AnyContext();
                }
                else
                {
                    logger.LogTrace("Default sleep for 50ms");
                    await timeProvider.Delay(TimeSpan.FromMilliseconds(50), acquireTimeoutCts.Token).AnyContext();
                }
            }
            catch (OperationCanceledException)
            {
                break;
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
                catch (OperationCanceledException)
                {
                    break;
                }
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

    public async Task<bool> IsLockedAsync(string resource)
    {
        var key = _GetKey(resource);
        var hitCounts = await storage.GetHitCountsAsync(key).AnyContext();

        return hitCounts > options.MaxHitsPerPeriod;
    }

    public async ValueTask DisposeAsync()
    {
        await storage.DisposeAsync().AnyContext();
    }

    #region Helpers

    private string _GetKey(string resource)
    {
        Argument.IsNotNullOrWhiteSpace(resource);

        resource = string.IsNullOrWhiteSpace(options.KeyPrefix) ? resource : $"{options.KeyPrefix}:{resource}";

        return $"{resource}:{_GetDateCurrentThrottlingPeriodStarted().Ticks.ToString(CultureInfo.InvariantCulture)}";
    }

    private DateTime _GetUtcDateTime()
    {
        return timeProvider.GetUtcNow().UtcDateTime;
    }

    private DateTime _GetDateCurrentThrottlingPeriodStarted()
    {
        var now = _GetUtcDateTime();
        var elapsedTicks = now.Ticks % options.ThrottlingPeriod.Ticks;

        return now.AddTicks(-elapsedTicks);
    }

    private DateTime _GetDateCurrentThrottlingPeriodEnded()
    {
        var now = _GetUtcDateTime();
        var elapsedTicks = now.Ticks % options.ThrottlingPeriod.Ticks;

        return now.AddTicks(options.ThrottlingPeriod.Ticks - elapsedTicks);
    }

    private void _LogCurrentInformation(string cacheKey)
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
    }

    private static CancellationTokenSource _GetAcquireCancellation(TimeSpan? timeout, CancellationToken token)
    {
        // Acquire timeout must be positive if not infinite.
        if (timeout is not null && timeout != Timeout.InfiniteTimeSpan)
        {
            Argument.IsPositiveOrZero(timeout.Value);
        }

        timeout ??= DefaultAcquireTimeout;
        var acquireTimeoutCts = CancellationTokenSource.CreateLinkedTokenSource(token);

        if (timeout != Timeout.InfiniteTimeSpan && timeout > TimeSpan.Zero)
        {
            acquireTimeoutCts.CancelAfter(timeout.Value);
        }

        return acquireTimeoutCts;
    }

    #endregion
}
