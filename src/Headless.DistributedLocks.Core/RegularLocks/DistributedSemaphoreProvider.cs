// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Collections.Concurrent;
using System.Diagnostics;
using Headless.Abstractions;
using Headless.Checks;
using Headless.Messaging;
using Microsoft.Extensions.Logging;
using Nito.AsyncEx;
using Polly;

#pragma warning disable IDE0130 // ReSharper disable once CheckNamespace
namespace Headless.DistributedLocks;

internal sealed class DistributedSemaphoreProvider(
    IDistributedSemaphoreStorage storage,
    IOutboxBus? outboxBus,
    DistributedLockOptions options,
    ILongIdGenerator longIdGenerator,
    TimeProvider timeProvider,
    ILogger<DistributedSemaphoreProvider> logger
) : IDistributedSemaphoreProvider, ICanReceiveLockReleased
{
    private static readonly TimeSpan _LongLockWarningThreshold = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan _NonBlockingAcquireDeadline = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan _OrphanSlotCleanupTimeout = TimeSpan.FromSeconds(5);
    private readonly IOutboxBus? _outboxBus = DistributedLockCoreHelpers.ConfigureOutboxBus(outboxBus, logger);
    private readonly ResiliencePipeline _releasePipeline = DistributedLockCoreHelpers.BuildReleasePipeline(timeProvider, logger);
    private readonly ConcurrentDictionary<string, ResetEventWithRefCount> _autoResetEvents = new(StringComparer.Ordinal);
    private readonly LeaseMonitorRegistry _monitorRegistry = new(logger);
    private readonly Lock _resetEventLock = new();
    private readonly int _maxResourceNameLength = options.MaxResourceNameLength;
    private readonly int? _maxConcurrentWaitingResources = options.MaxConcurrentWaitingResources;
    private readonly int? _maxWaitersPerResource = options.MaxWaitersPerResource;
    private readonly TimeSpan _disposeTimeout = options.DisposeTimeout;
    private readonly string _keyPrefix = options.KeyPrefix;

    public TimeSpan DefaultTimeUntilExpires { get; } = TimeSpan.FromMinutes(20);

    public TimeSpan DefaultAcquireTimeout { get; } = TimeSpan.FromSeconds(30);

    public IDistributedSemaphore CreateSemaphore(string resource, int maxCount)
    {
        _ValidateResource(resource);
        Argument.IsGreaterThanOrEqualTo(maxCount, 1);

        return new DistributedSemaphore(this, resource, maxCount);
    }

    internal async Task<IDistributedLock> AcquireAsync(
        string resource,
        int maxCount,
        DistributedLockAcquireOptions? options = null,
        CancellationToken cancellationToken = default
    )
    {
        var acquired = await TryAcquireAsync(resource, maxCount, options, cancellationToken).ConfigureAwait(false);

        return acquired
            ?? throw (
                options?.AcquireTimeout == TimeSpan.Zero
                    ? LockAcquisitionTimeoutException.ForTryOnceContention(resource)
                    : new LockAcquisitionTimeoutException(resource)
            );
    }

    internal async Task<IDistributedLock?> TryAcquireAsync(
        string resource,
        int maxCount,
        DistributedLockAcquireOptions? acquireOptions = null,
        CancellationToken cancellationToken = default
    )
    {
        _ValidateResource(resource);
        Argument.IsGreaterThanOrEqualTo(maxCount, 1);
        acquireOptions ??= new DistributedLockAcquireOptions();
        DistributedLockCoreHelpers.ValidateAcquireTimeout(acquireOptions.AcquireTimeout);

        // A semaphore slot is a ZSET member scored by its finite expiry timestamp; an infinite
        // lease has no score to plant. Unlike a mutex (single SET key that can omit PX), the slot
        // count is reclaimed only by pruning expired scores, so a non-expiring slot would hold
        // capacity forever. Reject Timeout.InfiniteTimeSpan up front regardless of monitoring mode
        // rather than letting NormalizeTimeUntilExpires silently cap it at the default.
        if (acquireOptions.TimeUntilExpires == Timeout.InfiniteTimeSpan)
        {
            throw new ArgumentException(
                "Distributed semaphore acquires require a finite timeUntilExpires; "
                    + "Timeout.InfiniteTimeSpan is not valid because a slot is stored with a finite expiry score.",
                nameof(acquireOptions)
            );
        }

        cancellationToken.ThrowIfCancellationRequested();

        var timeUntilExpires = DistributedLockCoreHelpers.NormalizeTimeUntilExpires(
            acquireOptions.TimeUntilExpires,
            DefaultTimeUntilExpires
        ) ?? DefaultTimeUntilExpires;
        var monitorLease = acquireOptions.Monitoring != LockMonitoringMode.None;
        var autoExtend = acquireOptions.Monitoring == LockMonitoringMode.AutoExtend;
        var leaseDuration = DistributedLockCoreHelpers.RequireFiniteLeaseDuration(timeUntilExpires, monitorLease);
        var acquireTimeout = acquireOptions.AcquireTimeout;
        var lockId = longIdGenerator.Create().ToString(CultureInfo.InvariantCulture);
        var timestamp = timeProvider.GetTimestamp();

        using var activity = _StartSemaphoreActivity(resource, maxCount);

        if (acquireTimeout == TimeSpan.Zero)
        {
            using var safetyCts = timeProvider.CreateCancellationTokenSource(_NonBlockingAcquireDeadline);
            using var linkedCts = cancellationToken.CanBeCanceled
                ? CancellationTokenSource.CreateLinkedTokenSource(safetyCts.Token, cancellationToken)
                : null;
            var attemptToken = linkedCts?.Token ?? safetyCts.Token;
            var singleResult = DistributedLockAcquireResult.Failed;

            try
            {
                singleResult = await _TryAcquireStorageAsync(resource, lockId, maxCount, timeUntilExpires, timestamp, attemptToken)
                    .ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                // Cancellation may have fired after storage accepted the ZADD but before we
                // received the reply; best-effort cleanup so we don't strand an orphan slot
                // (holding capacity) until the lease TTL expires.
                await _TryReleaseOrphanSlotAsync(resource, lockId).ConfigureAwait(false);

                if (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }

                singleResult = DistributedLockAcquireResult.Failed;
            }

            var singleWait = timeProvider.GetElapsedTime(timestamp);
            DistributedLockMetrics.SemaphoreWaitTime.Record(singleWait.TotalMilliseconds);

            if (!singleResult.Acquired)
            {
                DistributedLockMetrics.SemaphoreFailed.Add(1);
            }

            return singleResult.Acquired
                ? _CreateSlot(
                    resource,
                    lockId,
                    singleResult.FencingToken,
                    leaseDuration,
                    singleWait,
                    acquireOptions.ReleaseOnDispose,
                    monitorLease,
                    autoExtend
                )
                : null;
        }

        using var timeoutCts = timeProvider.CreateCancellationTokenSource(acquireTimeout ?? DefaultAcquireTimeout);
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(timeoutCts.Token, cancellationToken);
        ResetEventWithRefCount? autoResetEvent = null;
        var retryAttempt = 0;
        var isFirstAttempt = true;
        DistributedLockAcquireResult result = DistributedLockAcquireResult.Failed;

        try
        {
            do
            {
                // Very tight non-zero acquire timeouts can leave timeoutCts already cancelled by
                // the first attempt, preempting the storage call before it can run. Fall back to
                // the caller's bare token on that first attempt so acquisition gets one real
                // chance; retries use the linked token so the caller's budget governs the loop.
                // Mirrors DistributedLockProvider (Issue #282).
                var attemptToken = isFirstAttempt && timeoutCts.IsCancellationRequested ? cancellationToken : cts.Token;
                isFirstAttempt = false;

                result = await _TryAcquireStorageAsync(resource, lockId, maxCount, timeUntilExpires, timestamp, attemptToken)
                    .ConfigureAwait(false);

                if (result.Acquired)
                {
                    break;
                }

                if (cts.IsCancellationRequested)
                {
                    break;
                }

                autoResetEvent ??= _IncrementResetEvent(resource);
                var delayAmount = DistributedLockCoreHelpers.GetBackoffDelay(retryAttempt++);
                using var delayCts = timeProvider.CreateCancellationTokenSource(delayAmount);
                using var linkedCancellationTokenSource = CancellationTokenSource.CreateLinkedTokenSource(
                    delayCts.Token,
                    cts.Token
                );
                await autoResetEvent.Target.SafeWaitAsync(linkedCancellationTokenSource.Token).ConfigureAwait(false);
            } while (!cts.IsCancellationRequested);
        }
        catch (OperationCanceledException)
        {
            // Cancellation may have fired after storage accepted the ZADD but before we received
            // the reply; best-effort cleanup so we don't strand an orphan slot until lease TTL.
            await _TryReleaseOrphanSlotAsync(resource, lockId).ConfigureAwait(false);

            if (cancellationToken.IsCancellationRequested)
            {
                throw;
            }

            result = DistributedLockAcquireResult.Failed;
        }
        finally
        {
            _DecrementResetEvent(autoResetEvent, resource);
        }

        var timeWaitedForLock = timeProvider.GetElapsedTime(timestamp);
        DistributedLockMetrics.SemaphoreWaitTime.Record(timeWaitedForLock.TotalMilliseconds);

        if (!result.Acquired)
        {
            DistributedLockMetrics.SemaphoreFailed.Add(1);
            cancellationToken.ThrowIfCancellationRequested();
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

        return _CreateSlot(
            resource,
            lockId,
            result.FencingToken,
            leaseDuration,
            timeWaitedForLock,
            acquireOptions.ReleaseOnDispose,
            monitorLease,
            autoExtend
        );
    }

    internal Task<bool> RenewAsync(
        string resource,
        string lockId,
        TimeSpan? timeUntilExpires = null,
        CancellationToken cancellationToken = default
    )
    {
        _ValidateResource(resource);
        Argument.IsNotNullOrWhiteSpace(lockId);
        var ttl = DistributedLockCoreHelpers.NormalizeTimeUntilExpires(timeUntilExpires, DefaultTimeUntilExpires)
            ?? DefaultTimeUntilExpires;

        return storage.TryExtendAsync(_StorageResource(resource), lockId, ttl, cancellationToken).AsTask();
    }

    internal Task<bool> ValidateAsync(string resource, string lockId, CancellationToken cancellationToken = default)
    {
        _ValidateResource(resource);
        Argument.IsNotNullOrWhiteSpace(lockId);

        return storage.ValidateAsync(_StorageResource(resource), lockId, cancellationToken).AsTask();
    }

    internal async Task ReleaseAsync(
        string resource,
        string lockId,
        CancellationToken cancellationToken = default
    )
    {
        _ValidateResource(resource);
        Argument.IsNotNullOrWhiteSpace(lockId);
        var removed = false;

        try
        {
            removed = await _releasePipeline
                .ExecuteAsync(
                    static async (state, ct) =>
                    {
                        var (storage, storageResource, lockId) = state;
                        return await storage.ReleaseAsync(storageResource, lockId, ct).ConfigureAwait(false);
                    },
                    (storage, storageResource: _StorageResource(resource), lockId),
                    CancellationToken.None
                )
                .AsTask()
                .WaitAsync(_disposeTimeout, timeProvider, CancellationToken.None)
                .ConfigureAwait(false);
        }
        catch (TimeoutException)
        {
            logger.LogLockReleaseTimedOut(resource, lockId, _disposeTimeout);
        }

        var monitor = _monitorRegistry.TryDeregister(resource, lockId);
        if (monitor is not null)
        {
            await monitor.DisposeAsync().ConfigureAwait(false);
        }

        if (removed && _outboxBus is not null)
        {
            try
            {
                await _outboxBus
                    .PublishAsync(new DistributedLockReleased(resource, lockId), cancellationToken: cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (Exception exception)
            {
                logger.LogLockReleasePublishFailed(exception, resource, lockId);
            }
        }
    }

    public Task<long> GetHolderCountAsync(string resource, CancellationToken cancellationToken = default)
    {
        _ValidateResource(resource);

        return storage.GetCountAsync(_StorageResource(resource), cancellationToken).AsTask();
    }

    private async ValueTask<DistributedLockAcquireResult> _TryAcquireStorageAsync(
        string resource,
        string lockId,
        int maxCount,
        TimeSpan ttl,
        long startTimestamp,
        CancellationToken cancellationToken
    )
    {
        try
        {
            return await storage.TryAcquireAsync(_StorageResource(resource), lockId, maxCount, ttl, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception e) when (e is not (OperationCanceledException or ObjectDisposedException or InvalidOperationException))
        {
            logger.LogErrorAcquiringLockElapsed(e, resource, lockId, timeProvider, startTimestamp);

            // A transient backend failure may surface after the acquire script already committed
            // the ZADD; best-effort cleanup keyed on the unique lockId is idempotent and prevents
            // a stranded slot from holding capacity until the lease TTL expires.
            await _TryReleaseOrphanSlotAsync(resource, lockId).ConfigureAwait(false);

            return DistributedLockAcquireResult.Failed;
        }
    }

    private async Task _TryReleaseOrphanSlotAsync(string resource, string lockId)
    {
        try
        {
            using var cleanupCts = timeProvider.CreateCancellationTokenSource(_OrphanSlotCleanupTimeout);
            await storage.ReleaseAsync(_StorageResource(resource), lockId, cleanupCts.Token).ConfigureAwait(false);
        }
        catch (Exception e)
        {
            logger.LogBestEffortLockCleanupFailed(e, resource, lockId);
        }
    }

    private DisposableSemaphoreSlot _CreateSlot(
        string resource,
        string lockId,
        long? fencingToken,
        TimeSpan leaseDuration,
        TimeSpan timeWaitedForLock,
        bool releaseOnDispose,
        bool monitorLease,
        bool autoExtend
    )
    {
        var handle = new DisposableSemaphoreSlot(
            resource,
            lockId,
            fencingToken,
            leaseDuration,
            timeWaitedForLock,
            this,
            releaseOnDispose,
            autoExtend,
            options,
            timeProvider,
            _DeregisterMonitor,
            logger
        );

        if (!monitorLease)
        {
            return handle;
        }

#pragma warning disable CA2000
        var monitor = new LeaseMonitor(handle, timeProvider, logger);
#pragma warning restore CA2000
        _monitorRegistry.Register(resource, lockId, monitor);
        handle.AttachMonitor(monitor);

        return handle;
    }

    private void _ValidateResource(string resource)
    {
        Argument.IsNotNullOrWhiteSpace(resource);
        Argument.IsLessThanOrEqualTo(resource.Length, _maxResourceNameLength, paramName: nameof(resource));
    }

    private string _StorageResource(string resource)
    {
        return _keyPrefix + resource;
    }

    private ResetEventWithRefCount _IncrementResetEvent(string resource)
    {
        lock (_resetEventLock)
        {
            if (_autoResetEvents.TryGetValue(resource, out var existing))
            {
                if (_maxWaitersPerResource is { } max)
                {
                    Ensure.True(existing.RefCount < max, $"Maximum waiters per resource ({max}) exceeded");
                }

                existing.Increment();

                return existing;
            }

            if (_maxConcurrentWaitingResources is { } maxResources)
            {
                Ensure.True(
                    _autoResetEvents.Count < maxResources,
                    $"Maximum concurrent waiting resources ({maxResources}) exceeded"
                );
            }

            var newEvent = new ResetEventWithRefCount();
            _autoResetEvents[resource] = newEvent;

            return newEvent;
        }
    }

    private void _DecrementResetEvent(ResetEventWithRefCount? autoResetEvent, string resource)
    {
        if (autoResetEvent is null)
        {
            return;
        }

        lock (_resetEventLock)
        {
            var newCount = autoResetEvent.Decrement();
            if (
                newCount == 0
                && _autoResetEvents.TryGetValue(resource, out var existing)
                && ReferenceEquals(existing, autoResetEvent)
            )
            {
                _autoResetEvents.TryRemove(resource, out _);
            }
        }
    }

    void ICanReceiveLockReleased.OnLockReleased(DistributedLockReleased message)
    {
        if (_autoResetEvents.TryGetValue(message.Resource, out var autoResetEvent))
        {
            autoResetEvent.Target.Set();
        }

        _monitorRegistry.NudgeActive(message.Resource);
    }

    private void _DeregisterMonitor(string resource, string lockId)
    {
        _ = _monitorRegistry.TryDeregister(resource, lockId);
    }

    private static Activity? _StartSemaphoreActivity(string resource, int maxCount)
    {
        var activity = DistributedLocksDiagnostics.Start("semaphore.acquire");
        if (activity is null)
        {
            return null;
        }

        activity.AddTag("headless.lock.resource", resource);
        activity.AddTag("headless.semaphore.max_count", maxCount);
        activity.DisplayName = $"Semaphore: {resource}";

        return activity;
    }

    private sealed class DistributedSemaphore(DistributedSemaphoreProvider provider, string resource, int maxCount)
        : IDistributedSemaphore
    {
        public string Resource { get; } = resource;

        public int MaxCount { get; } = maxCount;

        public Task<IDistributedLock> AcquireAsync(
            DistributedLockAcquireOptions? options = null,
            CancellationToken cancellationToken = default
        )
        {
            return provider.AcquireAsync(Resource, MaxCount, options, cancellationToken);
        }

        public Task<IDistributedLock?> TryAcquireAsync(
            DistributedLockAcquireOptions? options = null,
            CancellationToken cancellationToken = default
        )
        {
            return provider.TryAcquireAsync(Resource, MaxCount, options, cancellationToken);
        }
    }

    private sealed class ResetEventWithRefCount
    {
        public int RefCount { get; private set; } = 1;

        public AsyncAutoResetEvent Target { get; } = new();

        public void Increment() => RefCount++;

        public int Decrement() => --RefCount;
    }
}
