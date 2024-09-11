using System.Collections.Concurrent;
using System.Diagnostics;
using System.Diagnostics.Metrics;
using Framework.Kernel.BuildingBlocks;
using Framework.Kernel.BuildingBlocks.Abstractions;
using Framework.Kernel.BuildingBlocks.Helpers.System;
using Framework.Messaging;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Nito.AsyncEx;

namespace Framework.ResourceLocks.Caching;

public sealed partial class StorageResourceLockProvider(
    IResourceLockStorage resourceLockStorage,
    IMessageBus messageBus,
    IClock clock,
    IUniqueLongGenerator longGenerator,
    ILogger<StorageResourceLockProvider> logger
) : IResourceLockProvider
{
    private bool _isSubscribed;
    private readonly AsyncLock _lock = new();
    private readonly ConcurrentDictionary<string, ResetEventWithRefCount> _resetEvents = new(StringComparer.Ordinal);

    private readonly Counter<int> _lockTimeoutCounter = FrameworkDiagnostics.Meter.CreateCounter<int>(
        "framework.lock.failed",
        description: "Number of failed attempts to acquire a lock"
    );

    private readonly Histogram<double> _lockWaitTimeHistogram = FrameworkDiagnostics.Meter.CreateHistogram<double>(
        "framework.lock.wait.time",
        unit: "ms",
        description: "Time waiting for locks"
    );

    // private readonly IResourceLockStorage _resourceLockStorage = new ScopedCacheClient(resourceLockStorage, "lock");

    public async Task<IResourceLock?> TryAcquireAsync(
        string resource,
        TimeSpan? timeUntilExpires = null,
        TimeSpan? acquireTimeout = null
    )
    {
        var isTraceLogLevelEnabled = logger.IsEnabled(LogLevel.Trace);
        var isDebugLogLevelEnabled = logger.IsEnabled(LogLevel.Debug);
        bool shouldWait = !cancellationToken.IsCancellationRequested;
        var lockId = longGenerator.Create().ToString(CultureInfo.InvariantCulture);
        timeUntilExpires ??= TimeSpan.FromMinutes(20);

        LogAttemptingToAcquireLock(logger, resource, lockId);
        using var activity = _StartLockActivity(resource);

        var gotLock = false;
        var sw = Stopwatch.StartNew();
        try
        {
            do
            {
                try
                {
                    gotLock =
                        timeUntilExpires.Value == TimeSpan.Zero
                            ? await resourceLockStorage.AddAsync(resource, lockId).AnyContext()
                            : await resourceLockStorage.AddAsync(resource, lockId, timeUntilExpires).AnyContext();
                }
                catch (Exception e)
                {
                    LogFailedAcquiringLock(logger, e, resource, lockId, sw.Elapsed);
                }

                if (gotLock)
                {
                    break;
                }

                if (isDebugLogLevelEnabled)
                {
                    logger.LogDebug("Failed to acquire lock {Resource} ({LockId})", resource, lockId);
                }

                if (cancellationToken.IsCancellationRequested)
                {
                    if (isTraceLogLevelEnabled && shouldWait)
                    {
                        logger.LogTrace(
                            "Cancellation requested while acquiring lock {Resource} ({LockId})",
                            resource,
                            lockId
                        );
                    }

                    break;
                }

                var autoResetEvent = _resetEvents.AddOrUpdate(
                    resource,
                    new ResetEventWithRefCount { RefCount = 1, Target = new AsyncAutoResetEvent() },
                    (n, e) =>
                    {
                        e.RefCount++;
                        return e;
                    }
                );
                if (!_isSubscribed)
                {
                    await _EnsureTopicSubscriptionAsync().AnyContext();
                }

                var keyExpiration = clock.Now.UtcDateTime.SafeAdd(
                    await resourceLockStorage.GetExpirationAsync(resource).AnyContext() ?? TimeSpan.Zero
                );
                var delayAmount = keyExpiration.Subtract(clock.Now.UtcDateTime);

                // delay a minimum of 50ms and a maximum of 3 seconds
                if (delayAmount < TimeSpan.FromMilliseconds(50))
                {
                    delayAmount = TimeSpan.FromMilliseconds(50);
                }
                else if (delayAmount > TimeSpan.FromSeconds(3))
                {
                    delayAmount = TimeSpan.FromSeconds(3);
                }

                if (isTraceLogLevelEnabled)
                {
                    logger.LogTrace(
                        "Will wait {Delay:g} before retrying to acquire lock {Resource} ({LockId})",
                        delayAmount,
                        resource,
                        lockId
                    );
                }

                // wait until we get a message saying the lock was released or 3 seconds has elapsed or cancellation has been requested
                using var linkedCancellationTokenSource = CancellationTokenSource.CreateLinkedTokenSource(
                    cancellationToken
                );
                linkedCancellationTokenSource.CancelAfter(delayAmount);

                try
                {
                    await autoResetEvent.Target.WaitAsync(linkedCancellationTokenSource.Token).AnyContext();
                }
                catch (OperationCanceledException) { }

                Thread.Yield();
            } while (!cancellationToken.IsCancellationRequested);
        }
        finally
        {
            var shouldRemove = false;
            _resetEvents.TryUpdate(
                resource,
                (n, e) =>
                {
                    e.RefCount--;
                    if (e.RefCount == 0)
                    {
                        shouldRemove = true;
                    }

                    return e;
                }
            );

            if (shouldRemove)
            {
                _resetEvents.TryRemove(resource, out var _);
            }
        }
        sw.Stop();

        _lockWaitTimeHistogram.Record(sw.Elapsed.TotalMilliseconds);

        if (!gotLock)
        {
            _lockTimeoutCounter.Add(1);

            if (cancellationToken.IsCancellationRequested && isTraceLogLevelEnabled)
            {
                logger.LogTrace(
                    "Cancellation requested for lock {Resource} ({LockId}) after {Duration:g}",
                    resource,
                    lockId,
                    sw.Elapsed
                );
            }
            else if (logger.IsEnabled(LogLevel.Warning))
            {
                logger.LogWarning(
                    "Failed to acquire lock {Resource} ({LockId}) after {Duration:g}",
                    resource,
                    lockId,
                    sw.Elapsed
                );
            }

            return null;
        }

        if (sw.Elapsed > TimeSpan.FromSeconds(5) && logger.IsEnabled(LogLevel.Warning))
        {
            logger.LogWarning("Acquired lock {Resource} ({LockId}) after {Duration:g}", resource, lockId, sw.Elapsed);
        }
        else if (logger.IsEnabled(LogLevel.Debug))
        {
            logger.LogDebug("Acquired lock {Resource} ({LockId}) after {Duration:g}", resource, lockId, sw.Elapsed);
        }

        return new DisposableLock(resource, lockId, sw.Elapsed, this, logger);
    }

    public async Task<bool> IsLockedAsync(string resource)
    {
        return await Run.WithRetriesAsync(() => resourceLockStorage.ExistsAsync(resource)).AnyContext();
    }

    public async Task ReleaseAsync(string resource, string lockId)
    {
        LogReleaseStarted(logger, resource, lockId);
        await Run.WithRetriesAsync(() => resourceLockStorage.RemoveIfEqualAsync(resource, lockId), 15).AnyContext();
        await messageBus.PublishAsync(new CacheLockReleased(resource, lockId)).AnyContext();
        LogReleaseReleased(logger, resource, lockId);
    }

    public Task<bool> RenewAsync(string resource, string lockId, TimeSpan? timeUntilExpires = null)
    {
        timeUntilExpires ??= TimeSpan.FromMinutes(20);
        LogRenewingLock(logger, resource, lockId, timeUntilExpires);

        return Run.WithRetriesAsync(
            () => resourceLockStorage.ReplaceIfEqualAsync(resource, lockId, lockId, timeUntilExpires.Value)
        );
    }

    #region Helpers

    private async Task _EnsureTopicSubscriptionAsync()
    {
        if (_isSubscribed)
        {
            return;
        }

        using (await _lock.LockAsync().AnyContext())
        {
            if (_isSubscribed)
            {
                return;
            }

            LogSubscribingToLockReleased(logger);
            await messageBus.SubscribeAsync<CacheLockReleased>(msg => _OnLockReleasedAsync(msg.Payload)).AnyContext();
            _isSubscribed = true;
            LogSubscribedToLockReleased(logger);
        }
    }

    private Task _OnLockReleasedAsync(CacheLockReleased released)
    {
        LogGotLockReleasedMessage(logger, released.Resource, released.LockId);

        if (_resetEvents.TryGetValue(released.Resource, out var autoResetEvent))
        {
            autoResetEvent.Target.Set();
        }

        return Task.CompletedTask;
    }

    private static Activity? _StartLockActivity(string resource)
    {
        var activity = FrameworkDiagnostics.ActivitySource.StartActivity(nameof(TryAcquireAsync));

        if (activity is null)
        {
            return null;
        }

        activity.AddTag("resource", resource);
        activity.DisplayName = $"Lock: {resource}";

        return activity;
    }

    [LoggerMessage(
        EventId = 1,
        EventName = "LockReleaseStarted",
        Level = LogLevel.Trace,
        Message = "ReleaseAsync Start: {Resource} ({LockId})"
    )]
    private static partial void LogReleaseStarted(ILogger logger, string resource, string lockId);

    [LoggerMessage(
        EventId = 2,
        EventName = "LockReleaseReleased",
        Level = LogLevel.Debug,
        Message = "Released lock: {Resource} ({LockId})"
    )]
    private static partial void LogReleaseReleased(ILogger logger, string resource, string lockId);

    [LoggerMessage(
        EventId = 3,
        EventName = "RenewingLock",
        Level = LogLevel.Debug,
        Message = "Renewing lock {Resource} ({LockId}) for {Duration:g}"
    )]
    private static partial void LogRenewingLock(ILogger logger, string resource, string lockId, TimeSpan? duration);

    [LoggerMessage(
        EventId = 4,
        EventName = "SubscribingToLockReleased",
        Level = LogLevel.Trace,
        Message = "Subscribing to cache lock released"
    )]
    private static partial void LogSubscribingToLockReleased(ILogger logger);

    [LoggerMessage(
        EventId = 5,
        EventName = "SubscribedToLockReleased",
        Level = LogLevel.Trace,
        Message = "Subscribed to cache lock released"
    )]
    private static partial void LogSubscribedToLockReleased(ILogger logger);

    [LoggerMessage(
        EventId = 6,
        EventName = "GotLockReleasedMessage",
        Level = LogLevel.Trace,
        Message = "Got lock released message: {Resource} ({LockId})"
    )]
    private static partial void LogGotLockReleasedMessage(ILogger logger, string resource, string lockId);

    [LoggerMessage(
        EventId = 7,
        EventName = "AttemptingToAcquireLock",
        Level = LogLevel.Debug,
        Message = "Attempting to acquire lock {Resource} ({LockId})"
    )]
    private static partial void LogAttemptingToAcquireLock(ILogger logger, string resource, string lockId);

    [LoggerMessage(
        EventId = 8,
        EventName = "FailedAcquiringLock",
        Level = LogLevel.Trace,
        Message = "Error acquiring lock {Resource} ({LockId})"
    )]
    private static partial void LogFailedAcquiringLock(
        ILogger logger,
        Exception ex,
        string resource,
        string lockId,
        TimeSpan duration
    );

    private sealed class ResetEventWithRefCount
    {
        public required int RefCount { get; set; }

        public required AsyncAutoResetEvent Target { get; set; }
    }

    #endregion
}

public sealed record CacheLockReleased(string Resource, string LockId);
