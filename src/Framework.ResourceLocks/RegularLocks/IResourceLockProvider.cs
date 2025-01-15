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
using Microsoft.Extensions.Options;
using Nito.AsyncEx;

#pragma warning disable IDE0130
// ReSharper disable once CheckNamespace
namespace Framework.ResourceLocks;

/// <summary>Provides methods to acquire, release, and manage resource locks.</summary>
[PublicAPI]
public interface IResourceLockProvider
{
    public TimeSpan DefaultTimeUntilExpires { get; }

    public TimeSpan DefaultAcquireTimeout { get; }

    /// <summary>
    /// Acquires a resource lock for a specified resource this method will block
    /// until the lock is acquired or the <paramref name="acquireTimeout"/> is reached.
    /// </summary>
    /// <param name="resource">The resource to acquire the lock for.</param>
    /// <param name="timeUntilExpires">
    /// The amount of time until the lock expires. The allowed values are:
    /// <list type="bullet">
    /// <item><see langword="null"/>: means the default value <see cref="DefaultTimeUntilExpires"/> (20 minutes).</item>
    /// <item><see cref="Timeout.InfiniteTimeSpan"/> (-1 milliseconds): means infinity no expiration set.</item>
    /// <item>Value greater than 0.</item>
    /// </list>
    /// </param>
    /// <param name="acquireTimeout">
    /// The amount of time to wait for the lock to be acquired. The allowed values are:
    /// <list type="bullet">
    /// <item><see langword="null"/>: means the default value <see cref="DefaultAcquireTimeout"/> (30 seconds).</item>
    /// <item><see cref="Timeout.InfiniteTimeSpan"/> (-1 millisecond): means infinity wait to acquire</item>
    /// <item>Value greater than or equal to 0.</item>
    /// </list>
    /// </param>
    /// <param name="acquireAbortToken"></param>
    /// <returns>
    /// A task that represents the asynchronous operation.
    /// The task result contains the acquired lock or null if the lock could not be acquired.
    /// </returns>
    Task<IResourceLock?> TryAcquireAsync(
        string resource,
        TimeSpan? timeUntilExpires = null,
        TimeSpan? acquireTimeout = null,
        CancellationToken acquireAbortToken = default
    );

    /// <summary>
    /// Renews a resource lock for a specified <paramref name="resource"/> by extending
    /// the expiration time of the lock if it is still held to the <paramref name="lockId"/>
    /// and return <see langword="true"/>, otherwise <see langword="false"/>.
    /// </summary>
    Task<bool> RenewAsync(
        string resource,
        string lockId,
        TimeSpan? timeUntilExpires = null,
        CancellationToken cancellationToken = default
    );

    /// <summary>
    /// Releases a resource lock for a specified <paramref name="resource"/>
    /// if it is acquired by the <paramref name="lockId"/>.
    /// </summary>
    Task ReleaseAsync(string resource, string lockId, CancellationToken cancellationToken = default);

    /// <summary>Checks if a specified resource is currently locked.</summary>
    Task<bool> IsLockedAsync(string resource, CancellationToken cancellationToken = default);
}

public sealed class ResourceLockProvider(
    IResourceLockStorage storage,
    IMessageBus messageBus,
    ILongIdGenerator idGenerator,
    TimeProvider timeProvider,
    IOptions<ResourceLockOptions> optionsAccessor,
    ILogger<ResourceLockProvider> logger
) : IResourceLockProvider
{
    private readonly ScopedResourceLockStorage _storage = new(storage, optionsAccessor);

    private readonly ConcurrentDictionary<string, ResetEventWithRefCount> _autoResetEvents = new(
        StringComparer.Ordinal
    );

    private readonly Counter<int> _lockTimeoutCounter = FrameworkDiagnostics.Meter.CreateCounter<int>(
        "framework.lock.failed",
        description: "Number of failed attempts to acquire a lock"
    );

    private readonly Histogram<double> _lockWaitTimeHistogram = FrameworkDiagnostics.Meter.CreateHistogram<double>(
        "framework.lock.wait.time",
        unit: "ms",
        description: "Time waiting for locks"
    );

    public TimeSpan DefaultTimeUntilExpires { get; } = TimeSpan.FromMinutes(20);

    public TimeSpan DefaultAcquireTimeout { get; } = TimeSpan.FromSeconds(30);

    #region Acquire

    public async Task<IResourceLock?> TryAcquireAsync(
        string resource,
        TimeSpan? timeUntilExpires = null,
        TimeSpan? acquireTimeout = null,
        CancellationToken acquireAbortToken = default
    )
    {
        Argument.IsNotNullOrWhiteSpace(resource);
        acquireAbortToken.ThrowIfCancellationRequested();

        var lockId = idGenerator.Create().ToString(CultureInfo.InvariantCulture);
        var timestamp = timeProvider.GetTimestamp();
        timeUntilExpires = _NormalizeTimeUntilExpires(timeUntilExpires);
        using var acquireTimeoutCts = _GetAcquireCts(acquireTimeout ?? DefaultAcquireTimeout, acquireAbortToken);
        using var activity = _StartLockActivity(resource);

        logger.LogAttemptingToAcquireLock(resource, lockId);

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

                if (acquireTimeoutCts.IsCancellationRequested)
                {
                    // Log only if cancellation was requested from the caller
                    if (!acquireAbortToken.IsCancellationRequested)
                    {
                        logger.LogCancellationRequested(resource, lockId);
                    }

                    break;
                }

                autoResetEvent ??= _IncrementResetEvent(resource);

                await _EnsureTopicSubscriptionAsync();

                var delayAmount = await _GetWaitAmount(resource);
                logger.LogDelayBeforeRetry(resource, lockId, delayAmount);

                await _WaitForResetOrTimeoutAsync(autoResetEvent, delayAmount, acquireTimeoutCts.Token);

                Thread.Yield();
            } while (!acquireTimeoutCts.IsCancellationRequested);
        }
        finally
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

        var timeWaitedForLock = timeProvider.GetElapsedTime(timestamp);
        _lockWaitTimeHistogram.Record(timeWaitedForLock.TotalMilliseconds);

        if (!gotLock)
        {
            _lockTimeoutCounter.Add(1);

            if (acquireAbortToken.IsCancellationRequested)
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

    private static async Task _WaitForResetOrTimeoutAsync(
        ResetEventWithRefCount resetEvent,
        TimeSpan delayAmount,
        CancellationToken acquireTimeoutToken
    )
    {
        // Wait until we get a message saying the lock was released by (autoResetEvent.Target.Set())
        // or delayAmount has elapsed
        // or acquire timeout cancellation has been requested

        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(acquireTimeoutToken);

        linkedCts.CancelAfter(delayAmount);

        try
        {
            await resetEvent.Target.WaitAsync(linkedCts.Token).AnyContext();
        }
        catch (OperationCanceledException)
        {
            // Ignore
        }
    }

    private static CancellationTokenSource _GetAcquireCts(TimeSpan timeout, CancellationToken token)
    {
        // Acquire timeout must be positive if not infinite.
        if (timeout != Timeout.InfiniteTimeSpan)
        {
            Argument.IsPositiveOrZero(timeout);
        }

        var acquireTimeoutCts = CancellationTokenSource.CreateLinkedTokenSource(token);

        if (timeout != Timeout.InfiniteTimeSpan)
        {
            acquireTimeoutCts.CancelAfter(timeout);
        }

        return acquireTimeoutCts;
    }

    private ResetEventWithRefCount _IncrementResetEvent(string resource)
    {
        var autoResetEvent = _autoResetEvents.AddOrUpdate(
            resource,
            new ResetEventWithRefCount(),
            (_, exist) =>
            {
                exist.Increment();

                return exist;
            }
        );

        return autoResetEvent;
    }

    private async Task<TimeSpan> _GetWaitAmount(string resource)
    {
        var currExpiration = await _storage.GetExpirationAsync(resource).AnyContext() ?? TimeSpan.Zero;

        // Delay a minimum of 50ms and a maximum of 3 seconds
        return currExpiration.Clamp(TimeSpan.FromMilliseconds(50), TimeSpan.FromSeconds(3));
    }

    #endregion

    #region Renew

    public async Task<bool> RenewAsync(
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

        return await Run.WithRetriesAsync(
            (_storage, resource, lockId, timeUntilExpires),
            static state =>
            {
                var (storage, resource, lockId, ttl) = state;

                return storage.ReplaceIfHasIdAsync(resource, lockId, lockId, ttl);
            },
            cancellationToken: cancellationToken
        );
    }

    #endregion

    #region Release

    public async Task ReleaseAsync(string resource, string lockId, CancellationToken cancellationToken = default)
    {
        Argument.IsNotNullOrWhiteSpace(resource);
        Argument.IsNotNullOrWhiteSpace(lockId);

        logger.LogReleaseStarted(resource, lockId);

        await Run.WithRetriesAsync(
            (_storage, resource, lockId),
            static state =>
            {
                var (storage, resource, lockId) = state;

                return storage.RemoveIfHasIdAsync(resource, lockId);
            },
            15,
            cancellationToken: cancellationToken
        );

        var storageLockReleased = new ResourceLockReleased { Resource = resource, LockId = lockId };
        await messageBus.PublishAsync(storageLockReleased, cancellationToken);

        logger.LogReleaseReleased(resource, lockId);
    }

    #endregion

    #region Is Locked

    public async Task<bool> IsLockedAsync(string resource, CancellationToken cancellationToken = default)
    {
        Argument.IsNotNullOrWhiteSpace(resource);

        return await Run.WithRetriesAsync(
                (_storage, resource),
                static x => x._storage.ExistsAsync(x.resource),
                cancellationToken: cancellationToken
            )
            .AnyContext();
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
        var activity = FrameworkDiagnostics.ActivitySource.StartActivity(nameof(TryAcquireAsync));

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

        public int RefCount => _refCount;

        public AsyncAutoResetEvent Target { get; } = new();

        public void Increment() => Interlocked.Increment(ref _refCount);

        public int Decrement() => Interlocked.Decrement(ref _refCount);
    }

    #endregion

    #region Ensure Lock Released Subscription

    private bool _isSubscribed;
    private readonly AsyncLock _subscribeLock = new();

    private async ValueTask _EnsureTopicSubscriptionAsync()
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

    private Task _OnLockReleasedAsync(ResourceLockReleased released)
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
