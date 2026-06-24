// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Collections.Concurrent;
using System.Diagnostics;
using Headless.Abstractions;
using Headless.Checks;
using Headless.Messaging;
using Microsoft.Extensions.Logging;
using Nito.AsyncEx;
using Polly;
using Polly.Retry;

#pragma warning disable IDE0130 // ReSharper disable once CheckNamespace
namespace Headless.DistributedLocks;

/// <summary>
/// The default mutex (exclusive) distributed-lock provider. Implements <see cref="IDistributedLock"/>
/// using a compare-and-set storage backend (<see cref="IDistributedLockStorage"/>) with exponential
/// backoff and optional push-based wake-up via the outbox bus.
/// </summary>
/// <remarks>
/// <para>
/// Acquire semantics: a successful storage insert (SET NX) atomically transfers ownership; the caller
/// receives an <see cref="IDistributedLease"/> handle that can be released explicitly or on dispose.
/// </para>
/// <para>
/// Wake-up model: when <see cref="IOutboxBus"/> is available, a <see cref="DistributedLockReleased"/>
/// message is published after each confirmed release; blocked acquirers are signalled immediately
/// instead of waiting for the next backoff interval. Without the bus, callers fall back to polling.
/// </para>
/// <para>
/// Monitoring: if <see cref="DistributedLockAcquireOptions.Monitoring"/> is
/// <see cref="LockMonitoringMode.Monitor"/> or <see cref="LockMonitoringMode.AutoExtend"/>, a
/// <see cref="LeaseMonitor"/> is attached to the returned handle. The monitor polls storage at the
/// configured cadence (and auto-extends the TTL when auto-extend is enabled); it cancels
/// <see cref="IDistributedLease.LostToken"/> when lease loss is detected.
/// </para>
/// <para>
/// This class also implements <see cref="ICanReceiveLockReleased"/> so the shared
/// <c>LockReleasedConsumer</c> can fan release signals out to multiple providers without coupling
/// to concrete types.
/// </para>
/// </remarks>
public sealed class DistributedLock(
    IDistributedLockStorage storage,
    IOutboxBus? outboxBus,
    DistributedLockOptions lockOptions,
    IGuidGenerator guidGenerator,
    TimeProvider timeProvider,
    ILogger<DistributedLock> logger
) : IDistributedLock, ICanReceiveLockReleased
{
    private readonly ScopedDistributedLockStorage _storage = new(storage, lockOptions.KeyPrefix);
    private readonly IOutboxBus? _outboxBus = DistributedLockCoreHelpers.ConfigureOutboxBus(outboxBus, logger);
    private readonly TimeSpan _disposeTimeout = lockOptions.DisposeTimeout;

    // Long-running pipeline for ReleaseAsync (critical path: failure to release strands waiters
    // until TTL expiry). 15 total attempts matches the prior `_MaxReleaseRetryAttempts`. Shared
    // helper so the reader-writer provider uses the same retry shape.
    private readonly ResiliencePipeline _releasePipeline = DistributedLockCoreHelpers.BuildReleasePipeline(
        timeProvider,
        logger
    );

    // Short pipeline for query/renew operations where 5 attempts mirror the prior default.
    private readonly ResiliencePipeline _queryPipeline = _BuildQueryPipeline(timeProvider, logger);

    private readonly ConcurrentDictionary<string, ResetEventWithRefCount> _autoResetEvents = new(
        StringComparer.Ordinal
    );

    private readonly LeaseMonitorRegistry _monitorRegistry = new(logger);

    private readonly Lock _resetEventLock = new();

    private static readonly TimeSpan _LongLockWarningThreshold = TimeSpan.FromSeconds(5);

    // Bounds the best-effort orphan-lock cleanup that runs when an acquire is cancelled
    // after the storage may have accepted the write. Worst-case interaction with the
    // Zero-path safety deadline below: if `_NonBlockingAcquireDeadline` fires AND cleanup
    // also stalls, the caller waits at most `_NonBlockingAcquireDeadline + _OrphanLockCleanupTimeout`
    // (currently 10s + 5s = 15s) before TryAcquireAsync returns. Kept smaller than the
    // acquire deadline because cleanup runs against a known-orphan record and should
    // succeed faster than a contended acquire.
    private static readonly TimeSpan _OrphanLockCleanupTimeout = TimeSpan.FromSeconds(5);

    // Ceiling on a single storage round-trip when `acquireTimeout: TimeSpan.Zero` is
    // requested (the "try once, no wait" semantic). Without this bound, a stalled
    // lock-store call would hang the caller indefinitely whenever the caller's
    // CancellationToken does not fire — for example, the messaging retry processor
    // which passes `context.CancellationToken` that only fires on shutdown. 10s is
    // 2× StackExchange.Redis's default AsyncTimeout, leaving headroom for transient
    // reconnects without re-introducing the indefinite-hang surface. Issue #297; F#2
    // from PR #284 review.
    private static readonly TimeSpan _NonBlockingAcquireDeadline = TimeSpan.FromSeconds(10);

    // Configurable limits from options
    private readonly int _maxResourceNameLength = lockOptions.MaxResourceNameLength;
    private readonly int? _maxConcurrentWaitingResources = lockOptions.MaxConcurrentWaitingResources;
    private readonly int? _maxWaitersPerResource = lockOptions.MaxWaitersPerResource;

    /// <summary>
    /// The lease TTL applied when <see cref="DistributedLockAcquireOptions.TimeUntilExpires"/> is
    /// <see langword="null"/> or <see cref="Timeout.InfiniteTimeSpan"/>. Default: 20 minutes.
    /// </summary>
    public TimeSpan DefaultTimeUntilExpires { get; } = TimeSpan.FromMinutes(20);

    /// <summary>
    /// The acquire-wait budget applied when <see cref="DistributedLockAcquireOptions.AcquireTimeout"/> is
    /// <see langword="null"/>. Default: 30 seconds.
    /// </summary>
    public TimeSpan DefaultAcquireTimeout { get; } = TimeSpan.FromSeconds(30);

    #region Acquire

    /// <inheritdoc/>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="resource"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">Thrown when <paramref name="resource"/> is empty or whitespace, or when
    /// monitoring is requested with an infinite lease TTL.</exception>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="resource"/> exceeds
    /// <see cref="DistributedLockOptions.MaxResourceNameLength"/>.</exception>
    /// <exception cref="LockAcquisitionTimeoutException">Thrown when the lock is not acquired before
    /// <see cref="DistributedLockAcquireOptions.AcquireTimeout"/> elapses.</exception>
    /// <exception cref="OperationCanceledException">Thrown when <paramref name="cancellationToken"/> is cancelled.</exception>
    public async Task<IDistributedLease> AcquireAsync(
        string resource,
        DistributedLockAcquireOptions? options = null,
        CancellationToken cancellationToken = default
    )
    {
        var acquired = await TryAcquireAsync(resource, options, cancellationToken).ConfigureAwait(false);

        if (acquired is not null)
        {
            return acquired;
        }

        throw options?.AcquireTimeout == TimeSpan.Zero
            ? LockAcquisitionTimeoutException.ForTryOnceContention(resource)
            : new LockAcquisitionTimeoutException(resource);
    }

    /// <inheritdoc/>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="resource"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">Thrown when <paramref name="resource"/> is empty or whitespace, or when
    /// monitoring is requested with an infinite lease TTL.</exception>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="resource"/> exceeds
    /// <see cref="DistributedLockOptions.MaxResourceNameLength"/>.</exception>
    /// <exception cref="OperationCanceledException">Thrown when <paramref name="cancellationToken"/> is cancelled.</exception>
    public async Task<IDistributedLease?> TryAcquireAsync(
        string resource,
        DistributedLockAcquireOptions? options = null,
        CancellationToken cancellationToken = default
    )
    {
        Argument.IsNotNullOrWhiteSpace(resource);
        Argument.IsLessThanOrEqualTo(resource.Length, _maxResourceNameLength, paramName: nameof(resource));
        options ??= new DistributedLockAcquireOptions();
        DistributedLockCoreHelpers.ValidateAcquireTimeout(options.AcquireTimeout);
        cancellationToken.ThrowIfCancellationRequested();

        var timeUntilExpires = DistributedLockCoreHelpers.NormalizeTimeUntilExpires(
            options.TimeUntilExpires,
            DefaultTimeUntilExpires
        );
        var acquireTimeout = options.AcquireTimeout;
        var releaseOnDispose = options.ReleaseOnDispose;
        var monitoring = options.Monitoring;
        var monitorLease = monitoring != LockMonitoringMode.None;
        var autoExtend = monitoring == LockMonitoringMode.AutoExtend;
        var leaseDuration = DistributedLockCoreHelpers.RequireFiniteLeaseDuration(timeUntilExpires, monitorLease);
        var leaseId = guidGenerator.Create().ToString("N");

        logger.LogAttemptingToAcquireLock(resource, leaseId);

        using var activity = _StartLockActivity(resource);
        var timestamp = timeProvider.GetTimestamp();

        // Fast path for the explicit "try once, no wait" semantic. Skips the
        // unconditional `timeoutCts` + linked `cts` pair (which would be created
        // already-cancelled for Zero) and the do-while retry loop (which would
        // exit on the first iteration anyway). Issue #297. Adds an internal
        // safety deadline (`_NonBlockingAcquireDeadline`) so the single storage
        // attempt cannot hang indefinitely when the caller's token does not
        // fire and the lock-store stalls — F#2 from PR #284 review.
        if (acquireTimeout == TimeSpan.Zero)
        {
            return await _TryAcquireOnceAsync(
                    resource,
                    leaseId,
                    timeUntilExpires,
                    timestamp,
                    releaseOnDispose,
                    monitorLease,
                    autoExtend,
                    leaseDuration,
                    cancellationToken
                )
                .ConfigureAwait(false);
        }

        using var timeoutCts = timeProvider.CreateCancellationTokenSource(acquireTimeout ?? DefaultAcquireTimeout);
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(timeoutCts.Token, cancellationToken);

        DistributedLockAcquireResult acquireResult = DistributedLockAcquireResult.Failed;
        ResetEventWithRefCount? autoResetEvent = null;
        var retryAttempt = 0;
        var isFirstAttempt = true;

        try
        {
            do
            {
                // Very tight non-zero `acquireTimeout` values (e.g., a few ms) can leave
                // `timeoutCts` already cancelled by the time we reach the first attempt,
                // which would preempt the storage call before it could complete. Fall back
                // to the caller's bare token in that race so the operation has at least one
                // real chance to acquire the lock. Subsequent retries always use the linked
                // token so the user's overall budget governs the wait loop. Issue #282;
                // tightened by review of #284.
                var attemptToken = isFirstAttempt && timeoutCts.IsCancellationRequested ? cancellationToken : cts.Token;
                isFirstAttempt = false;

                // Try to acquire the lock
                try
                {
                    acquireResult = await _storage
                        .InsertAsync(resource, leaseId, timeUntilExpires, attemptToken)
                        .ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    // Cancellation may have fired after the storage accepted the write but
                    // before we received the response. Attempt best-effort cleanup so we
                    // don't strand an orphan lock until TTL expiry.
                    await _TryReleaseOrphanLockAsync(resource, leaseId).ConfigureAwait(false);

                    if (cancellationToken.IsCancellationRequested)
                    {
                        throw;
                    }

                    break;
                }
                catch (Exception e) when (e is not (ObjectDisposedException or InvalidOperationException))
                {
                    // Swallow transient errors (network, timeout) and retry
                    logger.LogErrorAcquiringLockElapsed(e, resource, leaseId, timeProvider, timestamp);
                }

                if (acquireResult.Acquired)
                {
                    break;
                }

                // Failed to acquire lock either because it's already locked or an error occurred
                logger.LogFailedToAcquireLock(resource, leaseId);

                if (cts.IsCancellationRequested)
                {
                    // Log only if cancellation was requested from the caller
                    if (!cancellationToken.IsCancellationRequested)
                    {
                        logger.LogCancellationRequested(resource, leaseId);
                    }

                    break;
                }

                autoResetEvent ??= _IncrementResetEvent(resource);

                // Use exponential backoff instead of storage call
                var delayAmount = DistributedLockCoreHelpers.GetBackoffDelay(retryAttempt++);
                logger.LogDelayBeforeRetry(resource, leaseId, delayAmount);

                // Wait until we get a message saying the lock was released by (autoResetEvent.Target.Set())
                // or delayAmount has elapsed
                // or acquire timeout cancellation has been requested
                using var delayCts = timeProvider.CreateCancellationTokenSource(delayAmount);
                using var linkedCancellationTokenSource = CancellationTokenSource.CreateLinkedTokenSource(
                    delayCts.Token,
                    cts.Token
                );
                await autoResetEvent.Target.SafeWaitAsync(linkedCancellationTokenSource.Token).ConfigureAwait(false);
            } while (!cts.IsCancellationRequested);
        }
        finally
        {
            _DecrementResetEvent(autoResetEvent, resource);
        }

        var timeWaitedForLock = timeProvider.GetElapsedTime(timestamp);
        DistributedLockMetrics.LockWaitTime.Record(timeWaitedForLock.TotalMilliseconds);

        if (!acquireResult.Acquired)
        {
            DistributedLockMetrics.LockFailed.Add(1, DistributedLockMetrics.ReasonContended);

            if (cts.IsCancellationRequested)
            {
                logger.LogCancellationRequestedAfter(resource, leaseId, timeWaitedForLock);

                cancellationToken.ThrowIfCancellationRequested();
            }
            else
            {
                logger.LogFailedToAcquireLockAfter(resource, leaseId, timeWaitedForLock);
            }

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

        return _CreateLockHandle(
            resource,
            leaseId,
            acquireResult.FencingToken,
            leaseDuration,
            timeWaitedForLock,
            releaseOnDispose,
            monitorLease,
            autoExtend
        );
    }

    private async Task<IDistributedLease?> _TryAcquireOnceAsync(
        string resource,
        string leaseId,
        TimeSpan? timeUntilExpires,
        long timestamp,
        bool releaseOnDispose,
        bool monitorLease,
        bool autoExtend,
        TimeSpan leaseDuration,
        CancellationToken callerToken
    )
    {
        // Bound the single storage attempt by `_NonBlockingAcquireDeadline`.
        // When the caller cannot signal cancellation (CancellationToken.None /
        // default) we skip the linked CTS entirely — saves one CTS allocation
        // and the two registration nodes on messaging's hot path (the retry
        // processor passes a cancellable token, so the cheap path applies to
        // other consumers like Headless.Tus.DistributedLocks).
        using var safetyCts = timeProvider.CreateCancellationTokenSource(_NonBlockingAcquireDeadline);
        using var linkedCts = callerToken.CanBeCanceled
            ? CancellationTokenSource.CreateLinkedTokenSource(safetyCts.Token, callerToken)
            : null;

        var attemptToken = linkedCts?.Token ?? safetyCts.Token;
        DistributedLockAcquireResult acquireResult;
        var safetyDeadlineFired = false;

        try
        {
            acquireResult = await _storage
                .InsertAsync(resource, leaseId, timeUntilExpires, attemptToken)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // Best-effort orphan cleanup in case the storage accepted the
            // write but the response was preempted by the safety deadline
            // or caller cancellation.
            await _TryReleaseOrphanLockAsync(resource, leaseId).ConfigureAwait(false);

            if (callerToken.IsCancellationRequested)
            {
                throw;
            }

            // Caller has not cancelled, so an OCE here is the safety deadline firing
            // (the lock-store stalled past `_NonBlockingAcquireDeadline`). Confirm via the
            // safety CTS rather than inferring from the caller token alone, so an unrelated
            // storage-thrown OCE falls through to `reason=contended` instead of being
            // mislabeled a stall. Treat as "lock not acquired" (same null shape as a
            // contended try-once) but flag it so the failure surfaces a distinct EventId +
            // `reason=stalled` metric instead of looking like routine contention (#320).
            safetyDeadlineFired = safetyCts.IsCancellationRequested;
            acquireResult = DistributedLockAcquireResult.Failed;
        }
        catch (Exception e) when (e is not (ObjectDisposedException or InvalidOperationException))
        {
            logger.LogErrorAcquiringLockElapsed(e, resource, leaseId, timeProvider, timestamp);
            acquireResult = DistributedLockAcquireResult.Failed;
        }

        var timeWaitedForLock = timeProvider.GetElapsedTime(timestamp);
        DistributedLockMetrics.LockWaitTime.Record(timeWaitedForLock.TotalMilliseconds);

        if (!acquireResult.Acquired)
        {
            if (safetyDeadlineFired)
            {
                DistributedLockMetrics.LockFailed.Add(1, DistributedLockMetrics.ReasonStalled);
                logger.LogTryOnceSafetyDeadlineFired(resource, leaseId, timeWaitedForLock);
            }
            else
            {
                DistributedLockMetrics.LockFailed.Add(1, DistributedLockMetrics.ReasonContended);
                logger.LogFailedToAcquireLockAfter(resource, leaseId, timeWaitedForLock);
            }

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

        return _CreateLockHandle(
            resource,
            leaseId,
            acquireResult.FencingToken,
            leaseDuration,
            timeWaitedForLock,
            releaseOnDispose,
            monitorLease,
            autoExtend
        );
    }

    private DisposableDistributedLock _CreateLockHandle(
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
        var handle = new DisposableDistributedLock(
            resource,
            leaseId,
            fencingToken,
            leaseDuration,
            timeWaitedForLock,
            this,
            releaseOnDispose,
            autoExtend,
            lockOptions,
            timeProvider,
            _DeregisterMonitor,
            logger
        );

        if (!monitorLease)
        {
            return handle;
        }

#pragma warning disable CA2000 // Ownership is transferred to the returned handle and drained from DisposeAsync.
        var monitor = new LeaseMonitor(handle, timeProvider, logger);
#pragma warning restore CA2000
        _monitorRegistry.Register(resource, leaseId, monitor);
        handle.AttachMonitor(monitor);

        return handle;
    }

    private ResetEventWithRefCount _IncrementResetEvent(string resource)
    {
        lock (_resetEventLock)
        {
            if (_autoResetEvents.TryGetValue(resource, out var existing))
            {
                // Prevent unbounded waiters per resource (DoS protection)
                if (_maxWaitersPerResource is { } max)
                {
                    Ensure.True(existing.RefCount < max, $"Maximum waiters per resource ({max}) exceeded");
                }

                existing.Increment();

                return existing;
            }

            // Prevent unbounded unique resources (DoS protection)
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
            // Always decrement - we incremented when we got this reference
            var newCount = autoResetEvent.Decrement();

            // Only remove if this is still the current entry AND ref count hit zero
            if (
                newCount == 0
                && _autoResetEvents.TryGetValue(resource, out var exist)
                && ReferenceEquals(exist, autoResetEvent)
            )
            {
                _autoResetEvents.TryRemove(resource, out _);
            }
        }
    }

    private async Task _TryReleaseOrphanLockAsync(string resource, string leaseId)
    {
        try
        {
            using var cleanupCts = timeProvider.CreateCancellationTokenSource(_OrphanLockCleanupTimeout);
            await _storage.RemoveIfEqualAsync(resource, leaseId, cleanupCts.Token).ConfigureAwait(false);
        }
        catch (Exception e)
        {
            logger.LogBestEffortLockCleanupFailed(e, resource, leaseId);
        }
    }

    #endregion

    #region Release

    /// <inheritdoc/>
    /// <remarks>
    /// The storage release runs under <see cref="CancellationToken.None"/> (bounded by
    /// <see cref="DistributedLockOptions.DisposeTimeout"/>), so caller cancellation cannot abandon a
    /// half-completed release and strand the lock until its TTL — matching the reader-writer provider.
    /// The outbox publish uses <paramref name="cancellationToken"/>, but its failures (cancellation
    /// included) are swallowed and waiters simply fall back to polling backoff. Consequently this
    /// method never surfaces <see cref="OperationCanceledException"/>.
    /// </remarks>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="resource"/> or <paramref name="leaseId"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">Thrown when <paramref name="resource"/> or <paramref name="leaseId"/> is empty or whitespace.</exception>
    public async Task ReleaseAsync(string resource, string leaseId, CancellationToken cancellationToken = default)
    {
        Argument.IsNotNullOrWhiteSpace(resource);
        Argument.IsNotNullOrWhiteSpace(leaseId);

        logger.LogReleaseStarted(resource, leaseId);

        // If a transient exception fires AFTER storage has already deleted the row but BEFORE
        // we read the response, the retry's `RemoveIfEqualAsync` will return `false` (the
        // record is gone). Without latching `true`, the final attempt's `false` would suppress
        // the outbox publish and force waiters to fall back to polling backoff. We track
        // whether ANY attempt observed `true` and treat it as authoritative; the lambda mutates
        // `observedAttemptSucceeded` so success survives subsequent retries returning `false`.
        var observedAttemptSucceeded = false;

        var storageRef = _storage;
        var resourceRef = resource;
        var lockIdRef = leaseId;

        // Cap the release pipeline at DisposeTimeout so application shutdown is not blocked by
        // sustained storage unavailability. On timeout the pipeline continues in the background
        // and the storage's per-record TTL is the eventual consistency mechanism.
        bool removed;
        try
        {
            var lastResult = await _releasePipeline
                .ExecuteAsync(
                    async ct =>
                    {
                        var result = await storageRef
                            .RemoveIfEqualAsync(resourceRef, lockIdRef, ct)
                            .ConfigureAwait(false);

                        if (result)
                        {
                            observedAttemptSucceeded = true;
                        }

                        return result;
                    },
                    // Release is a terminal-state write: pass CancellationToken.None so caller
                    // cancellation cannot abandon a half-completed release and strand the lock until
                    // its TTL. Matches the reader-writer provider's release convention.
                    CancellationToken.None
                )
                .AsTask()
                .WaitAsync(_disposeTimeout, timeProvider, CancellationToken.None)
                .ConfigureAwait(false);

            removed = observedAttemptSucceeded || lastResult;
        }
        catch (TimeoutException)
        {
            logger.LogLockReleaseTimedOut(resource, leaseId, _disposeTimeout);
            // Background pipeline may still succeed and set observedAttemptSucceeded; treat the
            // caller's release as not-yet-confirmed so we skip the outbox publish (waiters will
            // fall back to polling, which is the same path as a never-published release).
            removed = false;
        }

        if (removed)
        {
            // Deregister after confirmed storage removal but before publishing the outbox
            // message. If release fails or retries are still in progress, the monitor must
            // remain visible so lease-loss detection continues for the still-held lock.
            var monitor = _monitorRegistry.TryDeregister(resource, leaseId);

            if (monitor is not null)
            {
                try
                {
                    await monitor.DisposeAsync().ConfigureAwait(false);
                }
                catch (Exception exception)
                {
                    logger.LogLeaseMonitorFaulted(exception, resource, leaseId);
                }
            }
        }

        // Only publish if we actually removed the lock.
        // Publish notifies waiters immediately; if skipped, waiters retry via backoff.
        if (removed && _outboxBus is not null)
        {
            var distributedLockReleased = new DistributedLockReleased(resource, leaseId);

            try
            {
                await _outboxBus
                    .PublishAsync(distributedLockReleased, cancellationToken: cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                // Release already succeeded — do not rethrow. Waiters will fall back to polling backoff.
                logger.LogLockReleasePublishFailed(ex, resource, leaseId);
            }
        }

        logger.LogReleaseReleased(resource, leaseId);
    }

    #endregion

    #region Renew

    /// <inheritdoc/>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="resource"/> or <paramref name="leaseId"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">Thrown when <paramref name="resource"/> or <paramref name="leaseId"/> is empty or whitespace.</exception>
    /// <exception cref="OperationCanceledException">Thrown when <paramref name="cancellationToken"/> is cancelled.</exception>
    public Task<bool> RenewAsync(
        string resource,
        string leaseId,
        TimeSpan? timeUntilExpires = null,
        CancellationToken cancellationToken = default
    )
    {
        Argument.IsNotNullOrWhiteSpace(resource);
        Argument.IsNotNullOrWhiteSpace(leaseId);

        timeUntilExpires = DistributedLockCoreHelpers.NormalizeTimeUntilExpires(
            timeUntilExpires,
            DefaultTimeUntilExpires
        );

        logger.LogRenewingLock(resource, leaseId, timeUntilExpires);

        return _queryPipeline
            .ExecuteAsync(
                static async (state, ct) =>
                {
                    var (storage, resource, leaseId, ttl) = state;

                    return await storage.ReplaceIfEqualAsync(resource, leaseId, leaseId, ttl, ct).ConfigureAwait(false);
                },
                (_storage, resource, leaseId, timeUntilExpires),
                cancellationToken
            )
            .AsTask();
    }

    #endregion

    #region Lease validation

    /// <inheritdoc/>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="resource"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">Thrown when <paramref name="resource"/> is empty or whitespace.</exception>
    /// <exception cref="OperationCanceledException">Thrown when <paramref name="cancellationToken"/> is cancelled.</exception>
    public Task<string?> GetLeaseIdAsync(string resource, CancellationToken cancellationToken = default)
    {
        Argument.IsNotNullOrWhiteSpace(resource);

        return _queryPipeline
            .ExecuteAsync(
                static async (state, ct) => await state._storage.GetAsync(state.resource, ct).ConfigureAwait(false),
                (_storage, resource),
                cancellationToken
            )
            .AsTask();
    }

    #endregion

    #region IsLocked

    /// <inheritdoc/>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="resource"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">Thrown when <paramref name="resource"/> is empty or whitespace.</exception>
    /// <exception cref="OperationCanceledException">Thrown when <paramref name="cancellationToken"/> is cancelled.</exception>
    public async Task<bool> IsLockedAsync(string resource, CancellationToken cancellationToken = default)
    {
        Argument.IsNotNullOrWhiteSpace(resource);

        return await _queryPipeline
            .ExecuteAsync(
                static async (state, ct) => await state._storage.ExistsAsync(state.resource, ct).ConfigureAwait(false),
                (_storage, resource),
                cancellationToken
            )
            .ConfigureAwait(false);
    }

    #endregion

    #region Observability

    /// <inheritdoc/>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="resource"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">Thrown when <paramref name="resource"/> is empty or whitespace.</exception>
    /// <exception cref="OperationCanceledException">Thrown when <paramref name="cancellationToken"/> is cancelled.</exception>
    public Task<TimeSpan?> GetExpirationAsync(string resource, CancellationToken cancellationToken = default)
    {
        Argument.IsNotNullOrWhiteSpace(resource);

        return _queryPipeline
            .ExecuteAsync(
                static async (state, ct) =>
                    await state._storage.GetExpirationAsync(state.resource, ct).ConfigureAwait(false),
                (_storage, resource),
                cancellationToken
            )
            .AsTask();
    }

    /// <inheritdoc/>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="resource"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">Thrown when <paramref name="resource"/> is empty or whitespace.</exception>
    /// <exception cref="OperationCanceledException">Thrown when <paramref name="cancellationToken"/> is cancelled.</exception>
    public async Task<DistributedLockInfo?> GetLockInfoAsync(
        string resource,
        CancellationToken cancellationToken = default
    )
    {
        Argument.IsNotNullOrWhiteSpace(resource);

        var leaseId = await _queryPipeline
            .ExecuteAsync(
                static async (state, ct) => await state._storage.GetAsync(state.resource, ct).ConfigureAwait(false),
                (_storage, resource),
                cancellationToken
            )
            .ConfigureAwait(false);

        if (leaseId is null)
        {
            return null;
        }

        var ttl = await _storage.GetExpirationAsync(resource, cancellationToken).ConfigureAwait(false);

        return new DistributedLockInfo
        {
            Resource = resource,
            LeaseId = leaseId,
            FencingToken = null,
            TimeToLive = ttl,
        };
    }

    /// <inheritdoc/>
    /// <exception cref="OperationCanceledException">Thrown when <paramref name="cancellationToken"/> is cancelled.</exception>
    public async Task<IReadOnlyList<DistributedLockInfo>> ListActiveLocksAsync(
        CancellationToken cancellationToken = default
    )
    {
        var locks = await _queryPipeline
            .ExecuteAsync(
                static async (storage, ct) =>
                    await storage.GetAllWithExpirationByPrefixAsync("", ct).ConfigureAwait(false),
                _storage,
                cancellationToken
            )
            .ConfigureAwait(false);

        var result = new List<DistributedLockInfo>(locks.Count);

        foreach (var (resource, info) in locks)
        {
            var lockInfo = new DistributedLockInfo
            {
                Resource = resource,
                LeaseId = info.LeaseId,
                FencingToken = null,
                TimeToLive = info.Ttl,
            };

            result.Add(lockInfo);
        }

        return result;
    }

    /// <inheritdoc/>
    /// <exception cref="OperationCanceledException">Thrown when <paramref name="cancellationToken"/> is cancelled.</exception>
    public Task<long> GetActiveLocksCountAsync(CancellationToken cancellationToken = default)
    {
        return _queryPipeline
            .ExecuteAsync(
                static async (storage, ct) => await storage.GetCountAsync("", ct).ConfigureAwait(false),
                _storage,
                cancellationToken
            )
            .AsTask();
    }

    #endregion

    #region Helpers

    private static Activity? _StartLockActivity(string resource)
    {
        var activity = DistributedLocksDiagnostics.Start("lock.acquire");

        if (activity is null)
        {
            return null;
        }

        activity.AddTag("headless.lock.resource", resource);
        activity.DisplayName = $"Lock: {resource}";

        return activity;
    }

    private static ResiliencePipeline _BuildQueryPipeline(TimeProvider timeProvider, ILogger<DistributedLock> logger)
    {
        return new ResiliencePipelineBuilder { TimeProvider = timeProvider }
            .AddRetry(
                new RetryStrategyOptions
                {
                    ShouldHandle = static args => new ValueTask<bool>(
                        args.Outcome.Exception is { } ex && DistributedLockCoreHelpers.IsTransientStorageException(ex)
                    ),
                    MaxRetryAttempts = 4, // 5 total attempts (1 initial + 4 retries)
                    BackoffType = DelayBackoffType.Exponential,
                    Delay = TimeSpan.FromMilliseconds(100),
                    MaxDelay = TimeSpan.FromSeconds(1),
                    UseJitter = true,
                    OnRetry = args =>
                    {
                        logger.LogLockStorageRetry(args.AttemptNumber + 1, args.RetryDelay, args.Outcome.Exception);

                        return default;
                    },
                }
            )
            .Build();
    }

    private sealed class ResetEventWithRefCount
    {
        public int RefCount { get; private set; } = 1;

        public AsyncAutoResetEvent Target { get; } = new();

        // No Interlocked needed - all access protected by _resetEventLock
        public void Increment() => RefCount++;

        public int Decrement() => --RefCount;
    }

    #endregion

    #region Message Consumer

    void ICanReceiveLockReleased.OnLockReleased(DistributedLockReleased message)
    {
        logger.LogGotLockReleasedMessage(message.Resource, message.LeaseId);

        // Signal waiters immediately when lock released instead of waiting for delay timeout.
        // No lock needed - ConcurrentDictionary.TryGetValue is thread-safe for reads.
        // Uses ref-counted events per resource to avoid memory leaks—events removed when no waiters.
        // ReSharper disable once InconsistentlySynchronizedField
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

    internal int GetActiveMonitorCount(string resource)
    {
        return _monitorRegistry.GetMonitorCount(resource);
    }

    internal int GetActiveMonitorResourceCount()
    {
        return _monitorRegistry.GetResourceCount();
    }

    /// <summary>
    /// Messaging consumer that fans a <see cref="DistributedLockReleased"/> event out to every
    /// registered <see cref="ICanReceiveLockReleased"/> provider (mutex and semaphore), allowing
    /// blocked acquirers to be woken immediately after a release rather than waiting for the next
    /// backoff poll interval.
    /// </summary>
    internal sealed class LockReleasedConsumer(
        IEnumerable<ICanReceiveLockReleased> receivers,
        ILogger<LockReleasedConsumer> logger
    ) : IConsume<DistributedLockReleased>
    {
        /// <summary>
        /// Fans the release signal to all registered receivers and returns synchronously.
        /// Cancellation is honoured before dispatch; if <paramref name="cancellationToken"/>
        /// is already cancelled, returns a cancelled <see cref="ValueTask"/> without calling
        /// any receiver.
        /// </summary>
        /// <param name="context">The consume context carrying the <see cref="DistributedLockReleased"/> message.</param>
        /// <param name="cancellationToken">Token cancelled on application shutdown.</param>
        /// <returns>A completed (or cancelled) <see cref="ValueTask"/>.</returns>
        public ValueTask ConsumeAsync(
            ConsumeContext<DistributedLockReleased> context,
            CancellationToken cancellationToken
        )
        {
            if (cancellationToken.IsCancellationRequested)
            {
                return ValueTask.FromCanceled(cancellationToken);
            }

            logger.LogProcessingLockReleased(context.MessageId, context.Message.Resource);

            // Fan the released signal out to every provider that can wake waiters (mutex,
            // semaphore, reader-writer). Both DistributedLock and
            // DistributedSemaphoreProvider are registered under ICanReceiveLockReleased via
            // TryAddEnumerable so this seam stays decoupled from the concrete provider types.
            foreach (var receiver in receivers)
            {
                receiver.OnLockReleased(context.Message);
            }

            return ValueTask.CompletedTask;
        }
    }

    #endregion
}
