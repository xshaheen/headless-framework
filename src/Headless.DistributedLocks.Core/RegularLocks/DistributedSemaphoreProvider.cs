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

/// <summary>
/// The distributed semaphore provider. Implements <see cref="IDistributedSemaphoreProvider"/> using
/// a scored-set storage backend (<see cref="IDistributedSemaphoreStorage"/>) where each slot is
/// stored with a finite expiry score, allowing the backend to reclaim capacity automatically when
/// TTLs expire.
/// </summary>
/// <remarks>
/// <para>
/// Semaphore slots require a finite TTL — <see cref="Timeout.InfiniteTimeSpan"/> is rejected at
/// acquire time because a non-expiring slot would hold capacity until an explicit release, which
/// breaks the capacity-reclaim contract.
/// </para>
/// <para>
/// Wake-up model mirrors <see cref="DistributedLock"/>: when <see cref="IOutboxBus"/> is available,
/// a <see cref="DistributedLockReleased"/> message is published after each confirmed release so
/// blocked acquirers are woken immediately. Without the bus, callers fall back to polling.
/// </para>
/// <para>
/// This class also implements <see cref="ICanReceiveLockReleased"/> so it participates in the same
/// fan-out signal path as the mutex provider via the shared <c>LockReleasedConsumer</c>.
/// </para>
/// </remarks>
internal sealed class DistributedSemaphoreProvider(
    IDistributedSemaphoreStorage storage,
    IOutboxBus? outboxBus,
    DistributedLockOptions options,
    IGuidGenerator guidGenerator,
    TimeProvider timeProvider,
    ILogger<DistributedSemaphoreProvider> logger
) : IDistributedSemaphoreProvider, ICanReceiveLockReleased
{
    private static readonly TimeSpan _LongLockWarningThreshold = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan _NonBlockingAcquireDeadline = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan _OrphanSlotCleanupTimeout = TimeSpan.FromSeconds(5);
    private readonly IOutboxBus? _outboxBus = DistributedLockCoreHelpers.ConfigureOutboxBus(outboxBus, logger);
    private readonly ResiliencePipeline _releasePipeline = DistributedLockCoreHelpers.BuildReleasePipeline(
        timeProvider,
        logger
    );
    private readonly ConcurrentDictionary<string, ResetEventWithRefCount> _autoResetEvents = new(
        StringComparer.Ordinal
    );
    private readonly LeaseMonitorRegistry _monitorRegistry = new(logger);
    private readonly Lock _resetEventLock = new();
    private readonly int _maxResourceNameLength = options.MaxResourceNameLength;
    private readonly int? _maxConcurrentWaitingResources = options.MaxConcurrentWaitingResources;
    private readonly int? _maxWaitersPerResource = options.MaxWaitersPerResource;
    private readonly TimeSpan _disposeTimeout = options.DisposeTimeout;
    private readonly string _keyPrefix = options.KeyPrefix;

    public TimeSpan DefaultTimeUntilExpires { get; } = TimeSpan.FromMinutes(20);

    public TimeSpan DefaultAcquireTimeout { get; } = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Creates a named semaphore view for the given resource and maximum slot count. The returned
    /// <see cref="IDistributedSemaphore"/> is a lightweight wrapper that delegates acquires back to
    /// this provider; no storage operation is performed here.
    /// </summary>
    /// <param name="resource">The resource name for the semaphore. Must be non-empty and within <see cref="DistributedLockOptions.MaxResourceNameLength"/>.</param>
    /// <param name="maxCount">The maximum number of concurrent slot holders. Must be &gt;= 1.</param>
    /// <returns>A new <see cref="IDistributedSemaphore"/> bound to this provider, <paramref name="resource"/>, and <paramref name="maxCount"/>.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="resource"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">Thrown when <paramref name="resource"/> is empty or whitespace.</exception>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="resource"/> exceeds
    /// <see cref="DistributedLockOptions.MaxResourceNameLength"/>, or when <paramref name="maxCount"/> is less than 1.</exception>
    public IDistributedSemaphore CreateSemaphore(string resource, int maxCount)
    {
        _ValidateResource(resource);
        Argument.IsGreaterThanOrEqualTo(maxCount, 1);

        return new DistributedSemaphore(this, resource, maxCount);
    }

    internal async Task<IDistributedLease> AcquireAsync(
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

    internal async Task<IDistributedLease?> TryAcquireAsync(
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

        var timeUntilExpires =
            DistributedLockCoreHelpers.NormalizeTimeUntilExpires(
                acquireOptions.TimeUntilExpires,
                DefaultTimeUntilExpires
            ) ?? DefaultTimeUntilExpires;
        var monitorLease = acquireOptions.Monitoring != LockMonitoringMode.None;
        var autoExtend = acquireOptions.Monitoring == LockMonitoringMode.AutoExtend;
        var leaseDuration = DistributedLockCoreHelpers.RequireFiniteLeaseDuration(timeUntilExpires, monitorLease);
        var acquireTimeout = acquireOptions.AcquireTimeout;
        var leaseId = guidGenerator.Create().ToString("N");
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
            var safetyDeadlineFired = false;

            try
            {
                singleResult = await _TryAcquireStorageAsync(
                        resource,
                        leaseId,
                        maxCount,
                        timeUntilExpires,
                        timestamp,
                        attemptToken
                    )
                    .ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                // Cancellation may have fired after storage accepted the ZADD but before we
                // received the reply; best-effort cleanup so we don't strand an orphan slot
                // (holding capacity) until the lease TTL expires.
                await _TryReleaseOrphanSlotAsync(resource, leaseId).ConfigureAwait(false);

                if (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }

                // Caller has not cancelled, so an OCE here is the safety deadline firing (the
                // slot store stalled past `_NonBlockingAcquireDeadline`). Confirm via the safety
                // CTS rather than the caller token alone, so an unrelated storage-thrown OCE
                // falls through to `reason=contended` instead of being mislabeled a stall (#320).
                safetyDeadlineFired = safetyCts.IsCancellationRequested;
                singleResult = DistributedLockAcquireResult.Failed;
            }

            var singleWait = timeProvider.GetElapsedTime(timestamp);
            DistributedLockMetrics.SemaphoreWaitTime.Record(singleWait.TotalMilliseconds);

            if (!singleResult.Acquired)
            {
                if (safetyDeadlineFired)
                {
                    DistributedLockMetrics.SemaphoreFailed.Add(1, DistributedLockMetrics.ReasonStalled);
                    logger.LogTryOnceSafetyDeadlineFired(resource, leaseId, singleWait);
                }
                else
                {
                    DistributedLockMetrics.SemaphoreFailed.Add(1, DistributedLockMetrics.ReasonContended);
                }
            }

            return singleResult.Acquired
                ? _CreateSlot(
                    resource,
                    leaseId,
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
                // Mirrors DistributedLock (Issue #282).
                var attemptToken = isFirstAttempt && timeoutCts.IsCancellationRequested ? cancellationToken : cts.Token;
                isFirstAttempt = false;

                result = await _TryAcquireStorageAsync(
                        resource,
                        leaseId,
                        maxCount,
                        timeUntilExpires,
                        timestamp,
                        attemptToken
                    )
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
            await _TryReleaseOrphanSlotAsync(resource, leaseId).ConfigureAwait(false);

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
            DistributedLockMetrics.SemaphoreFailed.Add(1, DistributedLockMetrics.ReasonContended);
            cancellationToken.ThrowIfCancellationRequested();
            return null;
        }

        if (timeWaitedForLock > _LongLockWarningThreshold)
        {
            logger.LogLongLockAcquired(resource, leaseId, timeWaitedForLock);
        }
        else
        {
            logger.LogAcquiredLock(resource, leaseId, timeWaitedForLock);
        }

        return _CreateSlot(
            resource,
            leaseId,
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
        string leaseId,
        TimeSpan? timeUntilExpires = null,
        CancellationToken cancellationToken = default
    )
    {
        _ValidateResource(resource);
        Argument.IsNotNullOrWhiteSpace(leaseId);
        var ttl =
            DistributedLockCoreHelpers.NormalizeTimeUntilExpires(timeUntilExpires, DefaultTimeUntilExpires)
            ?? DefaultTimeUntilExpires;

        return storage.TryExtendAsync(_StorageResource(resource), leaseId, ttl, cancellationToken).AsTask();
    }

    internal Task<bool> ValidateAsync(string resource, string leaseId, CancellationToken cancellationToken = default)
    {
        _ValidateResource(resource);
        Argument.IsNotNullOrWhiteSpace(leaseId);

        return storage.ValidateAsync(_StorageResource(resource), leaseId, cancellationToken).AsTask();
    }

    internal async Task ReleaseAsync(string resource, string leaseId, CancellationToken cancellationToken = default)
    {
        _ValidateResource(resource);
        Argument.IsNotNullOrWhiteSpace(leaseId);
        var removed = false;

        try
        {
            removed = await _releasePipeline
                .ExecuteAsync(
                    static async (state, ct) =>
                    {
                        var (storage, storageResource, leaseId) = state;
                        return await storage.ReleaseAsync(storageResource, leaseId, ct).ConfigureAwait(false);
                    },
                    (storage, storageResource: _StorageResource(resource), leaseId),
                    CancellationToken.None
                )
                .AsTask()
                .WaitAsync(_disposeTimeout, timeProvider, CancellationToken.None)
                .ConfigureAwait(false);
        }
        catch (TimeoutException)
        {
            logger.LogLockReleaseTimedOut(resource, leaseId, _disposeTimeout);
        }

        var monitor = _monitorRegistry.TryDeregister(resource, leaseId);
        if (monitor is not null)
        {
            await monitor.DisposeAsync().ConfigureAwait(false);
        }

        if (removed && _outboxBus is not null)
        {
            try
            {
                await _outboxBus
                    .PublishAsync(new DistributedLockReleased(resource, leaseId), cancellationToken: cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (Exception exception)
            {
                logger.LogLockReleasePublishFailed(exception, resource, leaseId);
            }
        }
    }

    /// <summary>
    /// Returns the number of active (non-expired) slot holders for the given <paramref name="resource"/>.
    /// </summary>
    /// <param name="resource">The semaphore resource name.</param>
    /// <param name="cancellationToken">Token to cancel the storage call.</param>
    /// <returns>The count of live slot holders, excluding expired entries.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="resource"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">Thrown when <paramref name="resource"/> is empty or whitespace.</exception>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="resource"/> exceeds <see cref="DistributedLockOptions.MaxResourceNameLength"/>.</exception>
    /// <exception cref="OperationCanceledException">Thrown when <paramref name="cancellationToken"/> is cancelled.</exception>
    public Task<long> GetHolderCountAsync(string resource, CancellationToken cancellationToken = default)
    {
        _ValidateResource(resource);

        return storage.GetCountAsync(_StorageResource(resource), cancellationToken).AsTask();
    }

    private async ValueTask<DistributedLockAcquireResult> _TryAcquireStorageAsync(
        string resource,
        string leaseId,
        int maxCount,
        TimeSpan ttl,
        long startTimestamp,
        CancellationToken cancellationToken
    )
    {
        try
        {
            return await storage
                .TryAcquireAsync(_StorageResource(resource), leaseId, maxCount, ttl, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception e)
            when (e is not (OperationCanceledException or ObjectDisposedException or InvalidOperationException))
        {
            logger.LogErrorAcquiringLockElapsed(e, resource, leaseId, timeProvider, startTimestamp);

            // A transient backend failure may surface after the acquire script already committed
            // the ZADD; best-effort cleanup keyed on the unique leaseId is idempotent and prevents
            // a stranded slot from holding capacity until the lease TTL expires.
            await _TryReleaseOrphanSlotAsync(resource, leaseId).ConfigureAwait(false);

            return DistributedLockAcquireResult.Failed;
        }
    }

    private async Task _TryReleaseOrphanSlotAsync(string resource, string leaseId)
    {
        try
        {
            using var cleanupCts = timeProvider.CreateCancellationTokenSource(_OrphanSlotCleanupTimeout);
            await storage.ReleaseAsync(_StorageResource(resource), leaseId, cleanupCts.Token).ConfigureAwait(false);
        }
        catch (Exception e)
        {
            logger.LogBestEffortLockCleanupFailed(e, resource, leaseId);
        }
    }

    private DisposableSemaphoreSlot _CreateSlot(
        string resource,
        string leaseId,
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
            leaseId,
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
        _monitorRegistry.Register(resource, leaseId, monitor);
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

    private void _DeregisterMonitor(string resource, string leaseId)
    {
        _ = _monitorRegistry.TryDeregister(resource, leaseId);
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

        public Task<IDistributedLease> AcquireAsync(
            DistributedLockAcquireOptions? options = null,
            CancellationToken cancellationToken = default
        )
        {
            return provider.AcquireAsync(Resource, MaxCount, options, cancellationToken);
        }

        public Task<IDistributedLease?> TryAcquireAsync(
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
