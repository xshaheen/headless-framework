// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Collections.Concurrent;
using System.Diagnostics;
using System.Diagnostics.Metrics;
using Framework.Abstractions;
using Framework.Checks;
using Framework.Constants;
using Framework.Core;
using Framework.Messaging;
using Humanizer;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Nito.AsyncEx;

namespace Framework.ResourceLocks.Storage.RegularLocks;

public sealed class StorageResourceLockProvider(
    IResourceLockStorage storage,
    IMessageBus messageBus,
    ILongIdGenerator longIdGenerator,
    TimeProvider timeProvider,
    ILogger<StorageResourceLockProvider> logger,
    IOptions<ResourceLockOptions> optionsAccessor
) : IResourceLockProvider
{
    private bool _isSubscribed;
    private readonly AsyncLock _lock = new();
    private readonly ConcurrentDictionary<string, ResetEventWithRefCount> _resetEvents = new(StringComparer.Ordinal);
    private readonly ScopedResourceLockStorage _storage = new(storage, optionsAccessor);

    private readonly Counter<int> _lockTimeoutCounter = FrameworkDiagnostics.Meter.CreateCounter<int>(
        "framework.lock.failed",
        description: "Number of failed attempts to acquire a lock"
    );

    private readonly Histogram<double> _lockWaitTimeHistogram = FrameworkDiagnostics.Meter.CreateHistogram<double>(
        "framework.lock.wait.time",
        unit: "ms",
        description: "Time waiting for locks"
    );

    public TimeSpan DefaultTimeUntilExpires => 20.Minutes();

    public TimeSpan DefaultAcquireTimeout => 30.Seconds();

    public async Task<IResourceLock?> TryAcquireAsync(
        string resource,
        TimeSpan? timeUntilExpires = null,
        TimeSpan? acquireTimeout = null,
        CancellationToken acquireAbortToken = default
    )
    {
        Argument.IsNotNullOrWhiteSpace(resource);

        using var activity = _StartLockActivity(resource);
        using var acquireTimeoutCts = _GetAcquireCancellation(acquireTimeout, acquireAbortToken);
        timeUntilExpires = _NormalizeTimeUntilExpires(timeUntilExpires);

        var lockId = longIdGenerator.Create().ToString(CultureInfo.InvariantCulture);

        logger.LogAttemptingToAcquireLock(resource, lockId);
        acquireAbortToken.ThrowIfCancellationRequested();

        var timestamp = timeProvider.GetTimestamp();
        var shouldWait = !acquireTimeoutCts.IsCancellationRequested;
        var gotLock = false;

        try
        {
            do
            {
                // Try to acquire the lock
                try
                {
                    gotLock = await _storage.InsertAsync(resource, lockId, timeUntilExpires).AnyContext();
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
                    addValue: new ResetEventWithRefCount(),
                    updateValueFactory: (_, existValue) =>
                    {
                        existValue.IncrementRefCount();
                        return existValue;
                    }
                );

                if (!_isSubscribed)
                {
                    await _EnsureTopicSubscriptionAsync().AnyContext();
                }

                var delayAmount = await _DelayAmountAsync(resource);

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
                    existValue.DecrementRefCount();

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

    public async Task<bool> RenewAsync(
        string resource,
        string lockId,
        TimeSpan? timeUntilExpires = null,
        CancellationToken cancellationToken = default
    )
    {
        Argument.IsNotNullOrWhiteSpace(resource);
        Argument.IsNotNullOrWhiteSpace(lockId);

        var expires = _NormalizeTimeUntilExpires(timeUntilExpires);
        logger.LogRenewingLock(resource, lockId, timeUntilExpires);

        return await Run.WithRetriesAsync(
            (_storage, resource, lockId, expires),
            static state =>
            {
                var (storage, resource, lockId, expires) = state;

                return storage.ReplaceIfEqualAsync(resource, lockId, lockId, expires);
            },
            cancellationToken: cancellationToken
        );
    }

    public async Task<bool> IsLockedAsync(string resource, CancellationToken cancellationToken = default)
    {
        return await Run.WithRetriesAsync(
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

        var storageLockReleased = new StorageLockReleased(resource, lockId);
        await messageBus.PublishAsync(storageLockReleased, cancellationToken: cancellationToken).AnyContext();

        logger.LogReleaseReleased(resource, lockId);
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

    private async Task<TimeSpan> _DelayAmountAsync(string resource)
    {
        var expiration = await _storage.GetExpirationAsync(resource).AnyContext() ?? TimeSpan.Zero;

        return expiration < TimeSpan.FromMilliseconds(50) ? TimeSpan.FromMilliseconds(50)
            : expiration > TimeSpan.FromSeconds(3) ? TimeSpan.FromSeconds(3)
            : expiration;
    }

    private TimeSpan? _NormalizeTimeUntilExpires(TimeSpan? timeUntilExpires)
    {
        return timeUntilExpires is null ? DefaultTimeUntilExpires
            : timeUntilExpires == Timeout.InfiniteTimeSpan ? null
            : Argument.IsPositive(timeUntilExpires.Value);
    }

    private CancellationTokenSource _GetAcquireCancellation(TimeSpan? timeout, CancellationToken token)
    {
        // Acquire timeout must be positive if not infinite.
        if (timeout is not null && timeout != Timeout.InfiniteTimeSpan)
        {
            Argument.IsPositiveOrZero(timeout.Value);
        }

        timeout ??= DefaultAcquireTimeout;
        var acquireTimeoutCts = CancellationTokenSource.CreateLinkedTokenSource(token);

        if (timeout != Timeout.InfiniteTimeSpan)
        {
            acquireTimeoutCts.CancelAfter(timeout.Value);
        }

        return acquireTimeoutCts;
    }

    private sealed class ResetEventWithRefCount
    {
        private int _refCount = 1;

        public int RefCount => _refCount;

        public AsyncAutoResetEvent Target { get; } = new();

        public void IncrementRefCount() => Interlocked.Increment(ref _refCount);

        public void DecrementRefCount() => Interlocked.Decrement(ref _refCount);
    }

    private sealed record StorageLockReleased(string Resource, string LockId);

    #endregion
}
