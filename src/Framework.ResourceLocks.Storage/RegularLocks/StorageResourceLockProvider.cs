// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Collections.Concurrent;
using System.Diagnostics;
using System.Diagnostics.Metrics;
using Framework.BuildingBlocks;
using Framework.BuildingBlocks.Abstractions;
using Framework.BuildingBlocks.Helpers.System;
using Framework.Checks;
using Framework.Messaging;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Nito.AsyncEx;

namespace Framework.ResourceLocks.Storage.RegularLocks;

public sealed class StorageResourceLockProvider(
    IResourceLockStorage storage,
    IMessageBus messageBus,
    IUniqueLongGenerator longGenerator,
    TimeProvider timeProvider,
    ILogger<StorageResourceLockProvider> logger,
    IOptions<ResourceLockOptions> optionsAccessor
) : IResourceLockProvider
{
    private readonly ScopedResourceLockStorage _storage = new(storage, optionsAccessor);
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

    public async Task<IResourceLock?> TryAcquireAsync(
        string resource,
        TimeSpan? timeUntilExpires = null,
        TimeSpan? acquireTimeout = null,
        CancellationToken acquireAbortToken = default
    )
    {
        Argument.IsNotNullOrWhiteSpace(resource);
        var normalizedTimeUntilExpires = _NormalizeTimeUntilExpires(timeUntilExpires);
        var normalizedAcquireTimeout = _NormalizeAcquireTimeout(acquireTimeout);

        var lockId = longGenerator.Create().ToString(CultureInfo.InvariantCulture);

        logger.LogAttemptingToAcquireLock(resource, lockId);
        using var activity = _StartLockActivity(resource);

        using var acquireTimeoutCts = CancellationTokenSource.CreateLinkedTokenSource(acquireAbortToken);

        if (normalizedAcquireTimeout is not null)
        {
            acquireTimeoutCts.CancelAfter(normalizedAcquireTimeout.Value);
        }

        var shouldWait = !acquireTimeoutCts.IsCancellationRequested;
        var timestamp = timeProvider.GetTimestamp();
        var gotLock = false;

        try
        {
            do
            {
                try
                {
                    gotLock = await _storage.InsertAsync(resource, lockId, normalizedTimeUntilExpires).AnyContext();
                }
                catch (Exception e)
                {
                    logger.LogErrorAcquiringLock(e, resource, lockId, timeProvider.GetElapsedTime(timestamp));
                }

                if (gotLock)
                {
                    break;
                }

                // Failed to acquire lock either because it's already locked or an error occurred
                logger.LogFailedToAcquireLock(resource, lockId);

                if (acquireTimeoutCts.IsCancellationRequested)
                {
                    if (shouldWait)
                    {
                        logger.LogCancellationRequested(resource, lockId);
                    }

                    break;
                }

                var autoResetEvent = _resetEvents.AddOrUpdate(
                    resource,
                    new ResetEventWithRefCount { RefCount = 1, Target = new AsyncAutoResetEvent() },
                    (_, existValue) =>
                    {
                        existValue.RefCount++;
                        return existValue;
                    }
                );

                if (!_isSubscribed)
                {
                    await _EnsureTopicSubscriptionAsync().AnyContext();
                }

                var lockPeriod = await _storage.GetExpirationAsync(resource).AnyContext() ?? TimeSpan.Zero;
                var delayAmount = _DelayAmount(lockPeriod);

                logger.LogDelayBeforeRetry(delayAmount, resource, lockId);

                // wait until we get a message saying the lock was released or 3 seconds has elapsed or cancellation has been requested
                using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(acquireTimeoutCts.Token);
                linkedCts.CancelAfter(delayAmount);

                try
                {
                    await autoResetEvent.Target.WaitAsync(linkedCts.Token).AnyContext();
                }
                catch (OperationCanceledException)
                {
                    // Ignore
                }

                Thread.Yield();
            } while (!acquireTimeoutCts.IsCancellationRequested);
        }
        finally
        {
            var shouldRemove = false;
            _resetEvents.TryUpdate(
                resource,
                (_, existValue) =>
                {
                    existValue.RefCount--;
                    if (existValue.RefCount == 0)
                    {
                        shouldRemove = true;
                    }

                    return existValue;
                }
            );

            if (shouldRemove)
            {
                _resetEvents.TryRemove(resource, out _);
            }
        }

        var timeWaitedForLock = timeProvider.GetElapsedTime(timestamp);
        _lockWaitTimeHistogram.Record(timeWaitedForLock.TotalMilliseconds);

        if (!gotLock)
        {
            _lockTimeoutCounter.Add(1);

            if (acquireTimeoutCts.IsCancellationRequested)
            {
                logger.LogCancellationRequestedAfter(resource, lockId, timeWaitedForLock);
            }
            else
            {
                logger.LogFailedToAcquireLockAfter(resource, lockId, timeWaitedForLock);
            }

            return null;
        }

        if (timeWaitedForLock > TimeSpan.FromSeconds(5))
        {
            logger.LogLongLockAcquired(resource, lockId, timeWaitedForLock);
        }
        else
        {
            logger.LogAcquiredLock(resource, lockId, timeWaitedForLock);
        }

        return new DisposableResourceLock(resource, lockId, timeWaitedForLock, this, logger, timeProvider);
    }

    public Task<bool> IsLockedAsync(string resource, CancellationToken cancellationToken = default)
    {
        return Run.WithRetriesAsync(
            (_storage, resource),
            static state => state._storage.ExistsAsync(state.resource),
            cancellationToken: cancellationToken
        );
    }

    public async Task ReleaseAsync(string resource, string lockId, CancellationToken cancellationToken = default)
    {
        logger.LogReleaseStarted(resource, lockId);

        await Run.WithRetriesAsync(
                (_storage, resource, lockId),
                static state =>
                {
                    var (storage, resource, lockId) = state;
                    return storage.RemoveIfEqualAsync(resource, lockId);
                },
                maxAttempts: 15,
                cancellationToken: cancellationToken
            )
            .AnyContext();

        await messageBus
            .PublishAsync(new StorageLockReleased(resource, lockId), cancellationToken: cancellationToken)
            .AnyContext();

        logger.LogReleaseReleased(resource, lockId);
    }

    public Task<bool> RenewAsync(
        string resource,
        string lockId,
        TimeSpan? timeUntilExpires = null,
        CancellationToken cancellationToken = default
    )
    {
        Argument.IsNotNullOrWhiteSpace(resource);
        Argument.IsNotNullOrWhiteSpace(lockId);
        var normalizedTimeUntilExpires = _NormalizeTimeUntilExpires(timeUntilExpires);

        logger.LogRenewingLock(resource, lockId, timeUntilExpires);

        return Run.WithRetriesAsync(
            (_storage, resource, lockId, normalizedTimeUntilExpires),
            static state =>
            {
                var (storage, resource, lockId, normalizedTimeUntilExpires) = state;

                return storage.ReplaceIfEqualAsync(resource, lockId, lockId, normalizedTimeUntilExpires);
            },
            cancellationToken: cancellationToken
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

            logger.LogSubscribingToLockReleased();
            await messageBus.SubscribeAsync<StorageLockReleased>(msg => _OnLockReleasedAsync(msg.Payload)).AnyContext();
            _isSubscribed = true;

            logger.LogSubscribedToLockReleased();
        }
    }

    private Task _OnLockReleasedAsync(StorageLockReleased released)
    {
        logger.LogGotLockReleasedMessage(released.Resource, released.LockId);

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

    /// <summary>Delay a minimum of 50 ms and a maximum of 3 seconds</summary>
    private static TimeSpan _DelayAmount(TimeSpan expiration)
    {
        return expiration < TimeSpan.FromMilliseconds(50) ? TimeSpan.FromMilliseconds(50)
            : expiration > TimeSpan.FromSeconds(3) ? TimeSpan.FromSeconds(3)
            : expiration;
    }

    private static TimeSpan? _NormalizeTimeUntilExpires(TimeSpan? timeUntilExpires)
    {
        return timeUntilExpires is null ? TimeSpan.FromMinutes(20)
            : timeUntilExpires == Timeout.InfiniteTimeSpan ? null
            : Argument.IsPositive(timeUntilExpires.Value);
    }

    private static TimeSpan? _NormalizeAcquireTimeout(TimeSpan? acquireTimeout)
    {
        return acquireTimeout is null ? TimeSpan.FromSeconds(30)
            : acquireTimeout == Timeout.InfiniteTimeSpan ? null
            : Argument.IsPositive(acquireTimeout.Value);
    }

    private sealed class ResetEventWithRefCount
    {
        public required int RefCount { get; set; }

        public required AsyncAutoResetEvent Target { get; init; }
    }

    #endregion
}
