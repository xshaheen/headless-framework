// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Collections.Concurrent;
using System.Diagnostics;
using System.Diagnostics.Metrics;
using Framework.Abstractions;
using Framework.Checks;
using Framework.Constants;
using Framework.Core;
using Framework.Messaging;
using Framework.ResourceLocks.RegularLocks;
using Microsoft.Extensions.Logging;
using Nito.AsyncEx;

#pragma warning disable IDE0130
// ReSharper disable once CheckNamespace
namespace Framework.ResourceLocks;

public sealed class ResourceLockProvider(
    IResourceLockStorage storage,
    IMessageBus messageBus,
    ResourceLockOptions options,
    ILongIdGenerator longIdGenerator,
    TimeProvider timeProvider,
    ILogger<ResourceLockProvider> logger
) : IResourceLockProvider, IHaveLogger, IHaveTimeProvider
{
    private readonly ScopedResourceLockStorage _storage = new(storage, options.KeyPrefix);

    private readonly ConcurrentDictionary<string, ResetEventWithRefCount> _autoResetEvents = new(
        StringComparer.Ordinal
    );

    private readonly Counter<int> _lockTimeoutCounter = HeadlessDiagnostics.Meter.CreateCounter<int>(
        "framework.lock.failed",
        description: "Number of failed attempts to acquire a lock"
    );

    private readonly Histogram<double> _lockWaitTimeHistogram = HeadlessDiagnostics.Meter.CreateHistogram<double>(
        "framework.lock.wait.time",
        unit: "ms",
        description: "Time waiting for locks"
    );

    ILogger IHaveLogger.Logger => logger;

    TimeProvider IHaveTimeProvider.TimeProvider => timeProvider;

    private static readonly TimeSpan _LongLockWarningThreshold = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan _MinRetryDelay = TimeSpan.FromMilliseconds(50);
    private static readonly TimeSpan _MaxRetryDelay = TimeSpan.FromSeconds(3);

    public TimeSpan DefaultTimeUntilExpires { get; } = TimeSpan.FromMinutes(20);

    public TimeSpan DefaultAcquireTimeout { get; } = TimeSpan.FromSeconds(30);

    #region Acquire

    public async Task<IResourceLock?> TryAcquireAsync(
        string resource,
        TimeSpan? timeUntilExpires = null,
        TimeSpan? acquireTimeout = null,
        CancellationToken cancellationToken = default
    )
    {
        Argument.IsNotNullOrWhiteSpace(resource);
        cancellationToken.ThrowIfCancellationRequested();

        timeUntilExpires = _NormalizeTimeUntilExpires(timeUntilExpires);
        var lockId = longIdGenerator.Create().ToString(CultureInfo.InvariantCulture);

        logger.LogAttemptingToAcquireLock(resource, lockId);

        using var cts = (acquireTimeout ?? DefaultAcquireTimeout).ToCancellationTokenSource(cancellationToken);
        using var activity = _StartLockActivity(resource);

        var timestamp = timeProvider.GetTimestamp();
        var gotLock = false;
        ResetEventWithRefCount? autoResetEvent = null;

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

                if (cts.IsCancellationRequested)
                {
                    // Log only if cancellation was requested from the caller
                    if (!cancellationToken.IsCancellationRequested)
                    {
                        logger.LogCancellationRequested(resource, lockId);
                    }

                    break;
                }

                autoResetEvent ??= _IncrementResetEvent(resource);

                if (!_isSubscribed)
                {
                    await _EnsureTopicSubscriptionAsync().AnyContext();
                }

                var delayAmount = await _GetWaitAmount(resource);
                logger.LogDelayBeforeRetry(resource, lockId, delayAmount);

                // Wait until we get a message saying the lock was released by (autoResetEvent.Target.Set())
                // or delayAmount has elapsed
                // or acquire timeout cancellation has been requested
                using var linkedCancellationTokenSource = delayAmount.ToCancellationTokenSource(cts.Token);
                await autoResetEvent.Target.SafeWaitAsync(linkedCancellationTokenSource.Token).AnyContext();
            } while (!cts.IsCancellationRequested);
        }
        finally
        {
            _DecrementResetEvent(autoResetEvent, resource);
        }

        var timeWaitedForLock = timeProvider.GetElapsedTime(timestamp);
        _lockWaitTimeHistogram.Record(timeWaitedForLock.TotalMilliseconds);

        if (!gotLock)
        {
            _lockTimeoutCounter.Add(1);

            if (cts.IsCancellationRequested)
            {
                logger.LogCancellationRequestedAfter(resource, lockId, timeWaitedForLock);
            }
            else
            {
                logger.LogFailedToAcquireLockAfter(resource, lockId, timeWaitedForLock);
            }

            return null;
        }

        if (timeWaitedForLock > _LongLockWarningThreshold)
        {
            logger.LogLongLockAcquired(resource, lockId, timeWaitedForLock);
        }
        else
        {
            logger.LogAcquiredLock(resource, lockId, timeWaitedForLock);
        }

        return new DisposableResourceLock(resource, lockId, timeWaitedForLock, this, timeProvider, logger);
    }

    private ResetEventWithRefCount _IncrementResetEvent(string resource)
    {
        return _autoResetEvents.AddOrUpdate(
            resource,
            static _ => new ResetEventWithRefCount(),
            static (_, exist) =>
            {
                exist.Increment();

                return exist;
            }
        );
    }

    private void _DecrementResetEvent(ResetEventWithRefCount? autoResetEvent, string resource)
    {
        if (
            autoResetEvent is not null
            && _autoResetEvents.TryGetValue(resource, out var exist)
            && exist == autoResetEvent
            && autoResetEvent.Decrement() == 0
        )
        {
            _autoResetEvents.TryRemove(resource, out _);
        }
    }

    private async Task<TimeSpan> _GetWaitAmount(string resource)
    {
        var exp = await _storage.GetExpirationAsync(resource).AnyContext() ?? TimeSpan.Zero;

        return exp.Clamp(_MinRetryDelay, _MaxRetryDelay);
    }

    #endregion

    #region Release

    public async Task ReleaseAsync(string resource, string lockId, CancellationToken cancellationToken = default)
    {
        Argument.IsNotNullOrWhiteSpace(resource);
        Argument.IsNotNullOrWhiteSpace(lockId);

        logger.LogReleaseStarted(resource, lockId);

        var removed = await Run.WithRetriesAsync(
                (_storage, resource, lockId),
                static state =>
                {
                    var (storage, resource, lockId) = state;

                    return storage.RemoveIfEqualAsync(resource, lockId);
                },
                maxAttempts: 15,
                timeProvider: timeProvider,
                cancellationToken: cancellationToken
            )
            .AnyContext();

        // Only publish if we actually removed the lock
        if (removed)
        {
            var resourceLockReleased = new ResourceLockReleased { Resource = resource, LockId = lockId };
            await messageBus.PublishAsync(resourceLockReleased, cancellationToken: cancellationToken).AnyContext();
        }

        logger.LogReleaseReleased(resource, lockId);
    }

    #endregion

    #region Renew

    public Task<bool> RenewAsync(
        string resource,
        string lockId,
        TimeSpan? timeUntilExpires = null,
        CancellationToken cancellationToken = default
    )
    {
        Argument.IsNotNullOrWhiteSpace(resource);
        Argument.IsNotNullOrWhiteSpace(lockId);

        timeUntilExpires = _NormalizeTimeUntilExpires(timeUntilExpires);

        logger.LogRenewingLock(resource, lockId, timeUntilExpires);

        return Run.WithRetriesAsync(
            (_storage, resource, lockId, timeUntilExpires),
            static state =>
            {
                var (storage, resource, lockId, ttl) = state;

                return storage.ReplaceIfEqualAsync(resource, lockId, lockId, ttl);
            },
            timeProvider: timeProvider,
            cancellationToken: cancellationToken
        );
    }

    #endregion

    #region IsLocked

    public async Task<bool> IsLockedAsync(string resource, CancellationToken cancellationToken = default)
    {
        Argument.IsNotNullOrWhiteSpace(resource);

        return await Run.WithRetriesAsync(
            (_storage, resource),
            static x => x._storage.ExistsAsync(x.resource),
            timeProvider: timeProvider,
            cancellationToken: cancellationToken
        );
    }

    #endregion

    #region Helpers

    private TimeSpan? _NormalizeTimeUntilExpires(TimeSpan? timeUntilExpires)
    {
        return timeUntilExpires is null ? DefaultTimeUntilExpires
            : timeUntilExpires == Timeout.InfiniteTimeSpan ? null
            : Argument.IsPositive(timeUntilExpires.Value);
    }

    private static Activity? _StartLockActivity(string resource)
    {
        var activity = HeadlessDiagnostics.ActivitySource.StartActivity(nameof(TryAcquireAsync));

        if (activity is null)
        {
            return null;
        }

        activity.AddTag("resource", resource);
        activity.DisplayName = $"Lock: {resource}";

        return activity;
    }

    private sealed class ResetEventWithRefCount
    {
        private int _refCount = 1;

        public AsyncAutoResetEvent Target { get; } = new();

        public void Increment() => Interlocked.Increment(ref _refCount);

        public int Decrement() => Interlocked.Decrement(ref _refCount);
    }

    #endregion

    #region Ensure Lock Released Subscription

    private bool _isSubscribed;
    private readonly AsyncLock _subscribeLock = new();

    private async Task _EnsureTopicSubscriptionAsync()
    {
        if (_isSubscribed)
        {
            return;
        }

        using (await _subscribeLock.LockAsync().AnyContext())
        {
            if (_isSubscribed)
            {
                return;
            }

            logger.LogSubscribingToLockReleased();

            await messageBus.SubscribeAsync<ResourceLockReleased>(_OnLockReleasedAsync).AnyContext();
            _isSubscribed = true;

            logger.LogSubscribedToLockReleased();
        }
    }

    private Task _OnLockReleasedAsync(ResourceLockReleased released, CancellationToken cancellationToken = default)
    {
        logger.LogGotLockReleasedMessage(released.Resource, released.LockId);

        if (_autoResetEvents.TryGetValue(released.Resource, out var autoResetEvent))
        {
            autoResetEvent.Target.Set();
        }

        return Task.CompletedTask;
    }

    #endregion
}
