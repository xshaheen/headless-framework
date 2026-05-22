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

public sealed class DistributedLockProvider(
    IDistributedLockStorage storage,
    IOutboxPublisher? outboxPublisher,
    DistributedLockOptions options,
    ILongIdGenerator longIdGenerator,
    TimeProvider timeProvider,
    ILogger<DistributedLockProvider> logger
) : IDistributedLockProvider, ICanReceiveLockReleased, IHaveLogger, IHaveTimeProvider
{
    private readonly ScopedDistributedLockStorage _storage = new(storage, options.KeyPrefix);
    private readonly IOutboxPublisher? _outboxPublisher = _ConfigureOutboxPublisher(outboxPublisher, logger);

    // Long-running pipeline for ReleaseAsync (critical path: failure to release strands waiters
    // until TTL expiry). 15 total attempts matches the prior `_MaxReleaseRetryAttempts`.
    private readonly ResiliencePipeline _releasePipeline = _BuildReleasePipeline(timeProvider, logger);

    // Short pipeline for query/renew operations where 5 attempts mirror the prior default.
    private readonly ResiliencePipeline _queryPipeline = _BuildQueryPipeline(timeProvider, logger);

    private readonly ConcurrentDictionary<string, ResetEventWithRefCount> _autoResetEvents = new(
        StringComparer.Ordinal
    );

    private readonly ConcurrentDictionary<string, ConcurrentDictionary<string, WeakReference<LeaseMonitor>>>
        _activeMonitors = new(StringComparer.Ordinal);

    private readonly Lock _resetEventLock = new();

    ILogger IHaveLogger.Logger => logger;

    TimeProvider IHaveTimeProvider.TimeProvider => timeProvider;

    private static readonly TimeSpan _LongLockWarningThreshold = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan _MinRetryDelay = TimeSpan.FromMilliseconds(50);
    private static readonly TimeSpan _MaxRetryDelay = TimeSpan.FromSeconds(3);

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
    private readonly int _maxResourceNameLength = options.MaxResourceNameLength;
    private readonly int? _maxConcurrentWaitingResources = options.MaxConcurrentWaitingResources;
    private readonly int? _maxWaitersPerResource = options.MaxWaitersPerResource;

    public TimeSpan DefaultTimeUntilExpires { get; } = TimeSpan.FromMinutes(20);

    public TimeSpan DefaultAcquireTimeout { get; } = TimeSpan.FromSeconds(30);

    #region Acquire

    public async Task<IDistributedLock> AcquireAsync(
        string resource,
        TimeSpan? timeUntilExpires = null,
        TimeSpan? acquireTimeout = null,
        bool releaseOnDispose = true,
        bool monitorLease = false,
        bool autoExtend = false,
        CancellationToken cancellationToken = default
    )
    {
        _ValidateAcquireTimeout(acquireTimeout);

        var acquired = await TryAcquireAsync(
                resource,
                timeUntilExpires,
                acquireTimeout,
                releaseOnDispose,
                monitorLease,
                autoExtend,
                cancellationToken
            )
            .ConfigureAwait(false);

        if (acquired is not null)
        {
            return acquired;
        }

        throw acquireTimeout == TimeSpan.Zero
            ? LockAcquisitionTimeoutException.ForTryOnceContention(resource)
            : new LockAcquisitionTimeoutException(resource);
    }

    public async Task<IDistributedLock?> TryAcquireAsync(
        string resource,
        TimeSpan? timeUntilExpires = null,
        TimeSpan? acquireTimeout = null,
        bool releaseOnDispose = true,
        bool monitorLease = false,
        bool autoExtend = false,
        CancellationToken cancellationToken = default
    )
    {
        Argument.IsNotNullOrWhiteSpace(resource);
        Argument.IsLessThanOrEqualTo(resource.Length, _maxResourceNameLength, paramName: nameof(resource));
        _ValidateAcquireTimeout(acquireTimeout);
        cancellationToken.ThrowIfCancellationRequested();

        timeUntilExpires = _NormalizeTimeUntilExpires(timeUntilExpires);
        var effectiveMonitorLease = monitorLease || autoExtend;
        var leaseDuration = _RequireFiniteLeaseDuration(timeUntilExpires, effectiveMonitorLease);
        var lockId = longIdGenerator.Create().ToString(CultureInfo.InvariantCulture);

        logger.LogAttemptingToAcquireLock(resource, lockId);

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
                    lockId,
                    timeUntilExpires,
                    timestamp,
                    releaseOnDispose,
                    effectiveMonitorLease,
                    autoExtend,
                    leaseDuration,
                    cancellationToken
                )
                .ConfigureAwait(false);
        }

        using var timeoutCts = timeProvider.CreateCancellationTokenSource(acquireTimeout ?? DefaultAcquireTimeout);
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(timeoutCts.Token, cancellationToken);

        var gotLock = false;
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
                    gotLock = await _storage.InsertAsync(resource, lockId, timeUntilExpires, attemptToken);
                }
                catch (OperationCanceledException)
                {
                    // Cancellation may have fired after the storage accepted the write but
                    // before we received the response. Attempt best-effort cleanup so we
                    // don't strand an orphan lock until TTL expiry.
                    await _TryReleaseOrphanLockAsync(resource, lockId).ConfigureAwait(false);

                    if (cancellationToken.IsCancellationRequested)
                    {
                        throw;
                    }

                    break;
                }
                catch (Exception e) when (e is not (ObjectDisposedException or InvalidOperationException))
                {
                    // Swallow transient errors (network, timeout) and retry
                    logger.LogErrorAcquiringLockElapsed(e, resource, lockId, timeProvider, timestamp);
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

                // Use exponential backoff instead of storage call
                var delayAmount = _GetBackoffDelay(retryAttempt++);
                logger.LogDelayBeforeRetry(resource, lockId, delayAmount);

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

        if (!gotLock)
        {
            DistributedLockMetrics.LockFailed.Add(1);

            if (cts.IsCancellationRequested)
            {
                logger.LogCancellationRequestedAfter(resource, lockId, timeWaitedForLock);

                cancellationToken.ThrowIfCancellationRequested();
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

        return _CreateLockHandle(
            resource,
            lockId,
            leaseDuration,
            timeWaitedForLock,
            releaseOnDispose,
            effectiveMonitorLease,
            autoExtend
        );
    }

    private async Task<IDistributedLock?> _TryAcquireOnceAsync(
        string resource,
        string lockId,
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
        bool gotLock;

        try
        {
            gotLock = await _storage
                .InsertAsync(resource, lockId, timeUntilExpires, attemptToken)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // Best-effort orphan cleanup in case the storage accepted the
            // write but the response was preempted by the safety deadline
            // or caller cancellation.
            await _TryReleaseOrphanLockAsync(resource, lockId).ConfigureAwait(false);

            if (callerToken.IsCancellationRequested)
            {
                throw;
            }

            // Safety deadline fired (caller has not cancelled). Treat as
            // "lock not acquired" — surface the same null shape as a normal
            // contended try-once result. Distinguishing this from a normal
            // contended result via a dedicated EventId is tracked by #320.
            gotLock = false;
        }
        catch (Exception e) when (e is not (ObjectDisposedException or InvalidOperationException))
        {
            logger.LogErrorAcquiringLockElapsed(e, resource, lockId, timeProvider, timestamp);
            gotLock = false;
        }

        var timeWaitedForLock = timeProvider.GetElapsedTime(timestamp);
        DistributedLockMetrics.LockWaitTime.Record(timeWaitedForLock.TotalMilliseconds);

        if (!gotLock)
        {
            DistributedLockMetrics.LockFailed.Add(1);
            logger.LogFailedToAcquireLockAfter(resource, lockId, timeWaitedForLock);
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

        return _CreateLockHandle(
            resource,
            lockId,
            leaseDuration,
            timeWaitedForLock,
            releaseOnDispose,
            monitorLease,
            autoExtend
        );
    }

    private DisposableDistributedLock _CreateLockHandle(
        string resource,
        string lockId,
        TimeSpan leaseDuration,
        TimeSpan timeWaitedForLock,
        bool releaseOnDispose,
        bool monitorLease,
        bool autoExtend
    )
    {
        var handle = new DisposableDistributedLock(
            resource,
            lockId,
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

#pragma warning disable CA2000 // Ownership is transferred to the returned handle and drained from DisposeAsync.
        var monitor = new LeaseMonitor(handle, timeProvider, logger);
#pragma warning restore CA2000
        _RegisterMonitor(resource, lockId, monitor);
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

    private async Task _TryReleaseOrphanLockAsync(string resource, string lockId)
    {
        try
        {
            using var cleanupCts = timeProvider.CreateCancellationTokenSource(_OrphanLockCleanupTimeout);
            await _storage.RemoveIfEqualAsync(resource, lockId, cleanupCts.Token).ConfigureAwait(false);
        }
        catch (Exception e)
        {
            logger.LogBestEffortLockCleanupFailed(e, resource, lockId);
        }
    }

    private static TimeSpan _GetBackoffDelay(int attempt)
    {
        // Exponential backoff: 50ms, 100ms, 200ms, 400ms, ... capped at 3s
        var delayMs = _MinRetryDelay.TotalMilliseconds * (1 << Math.Min(attempt, 6));
        var cappedDelayMs = Math.Min(delayMs, _MaxRetryDelay.TotalMilliseconds);

        // Add jitter (±25%) to prevent thundering herd
        var jitter = cappedDelayMs * ((Random.Shared.NextDouble() * 0.5) - 0.25);

        return TimeSpan.FromMilliseconds(cappedDelayMs + jitter);
    }

    #endregion

    #region Release

    public async Task ReleaseAsync(string resource, string lockId, CancellationToken cancellationToken = default)
    {
        Argument.IsNotNullOrWhiteSpace(resource);
        Argument.IsNotNullOrWhiteSpace(lockId);

        // Deregister the monitor BEFORE publishing the outbox message. Otherwise the outbox
        // consumer's _NudgeActiveMonitors would wake the still-registered monitor for the
        // released lockId — the monitor probes storage, sees the row gone (because we just
        // removed it), and declares Lost. That's a spurious signal on a self-release.
        _DeregisterMonitor(resource, lockId);

        logger.LogReleaseStarted(resource, lockId);

        // If a transient exception fires AFTER storage has already deleted the row but BEFORE
        // we read the response, the retry's `RemoveIfEqualAsync` will return `false` (the
        // record is gone). Without latching `true`, the final attempt's `false` would suppress
        // the outbox publish and force waiters to fall back to polling backoff. We track
        // whether ANY attempt observed `true` and treat it as authoritative; the lambda mutates
        // `observedAttemptSucceeded` so success survives subsequent retries returning `false`.
        var observedAttemptSucceeded = false;

        var storageRef = _storage;
        var resourceRef = resource;
        var lockIdRef = lockId;

        var lastResult = await _releasePipeline
            .ExecuteAsync(
                async ct =>
                {
                    var result = await storageRef.RemoveIfEqualAsync(resourceRef, lockIdRef, ct).ConfigureAwait(false);

                    if (result)
                    {
                        observedAttemptSucceeded = true;
                    }

                    return result;
                },
                cancellationToken
            )
            .ConfigureAwait(false);

        var removed = observedAttemptSucceeded || lastResult;

        // Only publish if we actually removed the lock.
        // Publish notifies waiters immediately; if skipped, waiters retry via backoff.
        if (removed && _outboxPublisher is not null)
        {
            var distributedLockReleased = new DistributedLockReleased(resource, lockId);

            try
            {
                await _outboxPublisher
                    .PublishAsync(distributedLockReleased, cancellationToken: cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                // Release already succeeded — do not rethrow. Waiters will fall back to polling backoff.
                logger.LogLockReleasePublishFailed(ex, resource, lockId);
            }
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

        return _queryPipeline
            .ExecuteAsync(
                static async (state, ct) =>
                {
                    var (storage, resource, lockId, ttl) = state;

                    return await storage.ReplaceIfEqualAsync(resource, lockId, lockId, ttl, ct).ConfigureAwait(false);
                },
                (_storage, resource, lockId, timeUntilExpires),
                cancellationToken
            )
            .AsTask();
    }

    #endregion

    #region Lease validation

    public Task<string?> GetLockIdAsync(string resource, CancellationToken cancellationToken = default)
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

    public async Task<LockInfo?> GetLockInfoAsync(string resource, CancellationToken cancellationToken = default)
    {
        Argument.IsNotNullOrWhiteSpace(resource);

        var lockId = await _queryPipeline
            .ExecuteAsync(
                static async (state, ct) => await state._storage.GetAsync(state.resource, ct).ConfigureAwait(false),
                (_storage, resource),
                cancellationToken
            )
            .ConfigureAwait(false);

        if (lockId is null)
        {
            return null;
        }

        var ttl = await _storage.GetExpirationAsync(resource, cancellationToken).ConfigureAwait(false);

        return new LockInfo
        {
            Resource = resource,
            LockId = lockId,
            TimeToLive = ttl,
        };
    }

    public async Task<IReadOnlyList<LockInfo>> ListActiveLocksAsync(CancellationToken cancellationToken = default)
    {
        var locks = await _queryPipeline
            .ExecuteAsync(
                static async (storage, ct) =>
                    await storage.GetAllWithExpirationByPrefixAsync("", ct).ConfigureAwait(false),
                _storage,
                cancellationToken
            )
            .ConfigureAwait(false);

        var result = new List<LockInfo>(locks.Count);

        foreach (var (resource, info) in locks)
        {
            var lockInfo = new LockInfo
            {
                Resource = resource,
                LockId = info.LockId,
                TimeToLive = info.Ttl,
            };

            result.Add(lockInfo);
        }

        return result;
    }

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

    private TimeSpan? _NormalizeTimeUntilExpires(TimeSpan? timeUntilExpires)
    {
        return timeUntilExpires is null ? DefaultTimeUntilExpires
            : timeUntilExpires == Timeout.InfiniteTimeSpan ? null
            : Argument.IsPositive(timeUntilExpires.Value);
    }

    private static TimeSpan _RequireFiniteLeaseDuration(TimeSpan? timeUntilExpires, bool monitorLease)
    {
        if (timeUntilExpires is { } leaseDuration)
        {
            return leaseDuration;
        }

        if (monitorLease)
        {
            // Lease monitoring requires a finite timeUntilExpires; null encodes the
            // Timeout.InfiniteTimeSpan sentinel here. Use Argument.IsNotNull to surface
            // the framework's standard ArgumentNullException with paramName set.
            Argument.IsNotNull(timeUntilExpires, paramName: nameof(timeUntilExpires));
        }

        return Timeout.InfiniteTimeSpan;
    }

    private static void _ValidateAcquireTimeout(TimeSpan? acquireTimeout)
    {
        // Allow null (use default) and Timeout.InfiniteTimeSpan as the explicit "wait forever" sentinel;
        // reject other negatives.
        if (acquireTimeout is null || acquireTimeout == Timeout.InfiniteTimeSpan)
        {
            return;
        }

        Argument.IsPositiveOrZero(acquireTimeout.Value, paramName: nameof(acquireTimeout));
    }

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

    private static IOutboxPublisher? _ConfigureOutboxPublisher(
        IOutboxPublisher? outboxPublisher,
        ILogger<DistributedLockProvider> logger
    )
    {
        if (outboxPublisher is null)
        {
            logger.LogOutboxPublisherAbsent();
        }

        return outboxPublisher;
    }

    // Transient = anything that isn't a programmer error or caller-driven cancellation.
    // Mirrors the existing catch filter used by the acquire loop (line ~185).
    private static bool _IsTransientStorageException(Exception ex)
    {
        return ex
            is not (
                OperationCanceledException
                or ObjectDisposedException
                or InvalidOperationException
                or ArgumentException
            );
    }

    private static ResiliencePipeline _BuildReleasePipeline(
        TimeProvider timeProvider,
        ILogger<DistributedLockProvider> logger
    )
    {
        return new ResiliencePipelineBuilder { TimeProvider = timeProvider }
            .AddRetry(
                new RetryStrategyOptions
                {
                    ShouldHandle = static args => new ValueTask<bool>(
                        args.Outcome.Exception is { } ex && _IsTransientStorageException(ex)
                    ),
                    MaxRetryAttempts = 14, // 15 total attempts (1 initial + 14 retries)
                    BackoffType = DelayBackoffType.Exponential,
                    Delay = TimeSpan.FromMilliseconds(50),
                    MaxDelay = TimeSpan.FromSeconds(2),
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

    private static ResiliencePipeline _BuildQueryPipeline(
        TimeProvider timeProvider,
        ILogger<DistributedLockProvider> logger
    )
    {
        return new ResiliencePipelineBuilder { TimeProvider = timeProvider }
            .AddRetry(
                new RetryStrategyOptions
                {
                    ShouldHandle = static args => new ValueTask<bool>(
                        args.Outcome.Exception is { } ex && _IsTransientStorageException(ex)
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
        logger.LogGotLockReleasedMessage(message.Resource, message.LockId);

        // Signal waiters immediately when lock released instead of waiting for delay timeout.
        // No lock needed - ConcurrentDictionary.TryGetValue is thread-safe for reads.
        // Uses ref-counted events per resource to avoid memory leaks—events removed when no waiters.
        // ReSharper disable once InconsistentlySynchronizedField
        if (_autoResetEvents.TryGetValue(message.Resource, out var autoResetEvent))
        {
            autoResetEvent.Target.Set();
        }

        _NudgeActiveMonitors(message.Resource);
    }

    private void _RegisterMonitor(string resource, string lockId, LeaseMonitor monitor)
    {
        var monitors = _activeMonitors.GetOrAdd(
            resource,
            static _ => new ConcurrentDictionary<string, WeakReference<LeaseMonitor>>(StringComparer.Ordinal)
        );

        monitors[lockId] = new WeakReference<LeaseMonitor>(monitor);
        logger.LogLeaseMonitorRegistered(resource, lockId);
    }

    private void _DeregisterMonitor(string resource, string lockId)
    {
        if (!_activeMonitors.TryGetValue(resource, out var monitors))
        {
            return;
        }

        monitors.TryRemove(lockId, out _);
        logger.LogLeaseMonitorDeregistered(resource, lockId);

        // Intentionally do NOT TryRemove the outer dict entry when the inner is empty:
        // an `IsEmpty` + `TryRemove` pair is not atomic on ConcurrentDictionary, and a
        // racing registration could repopulate the inner map between the two calls,
        // leaving us with a removed-but-still-referenced inner map. Dead WeakReferences
        // are cleared opportunistically in _NudgeActiveMonitors, and the GC reclaims
        // empty per-resource maps naturally once no future acquire hits that resource.
    }

    private void _NudgeActiveMonitors(string resource)
    {
        if (!_activeMonitors.TryGetValue(resource, out var monitors))
        {
            return;
        }

        foreach (var (lockId, weakReference) in monitors)
        {
            if (weakReference.TryGetTarget(out var monitor))
            {
                monitor.TriggerImmediateValidation();
                logger.LogLeaseMonitorNudged(resource, lockId);

                continue;
            }

            monitors.TryRemove(lockId, out _);
        }
    }

    internal int GetActiveMonitorCount(string resource)
    {
        return _activeMonitors.TryGetValue(resource, out var monitors) ? monitors.Count : 0;
    }

    internal sealed class LockReleasedConsumer(ICanReceiveLockReleased receiver, ILogger<LockReleasedConsumer> logger)
        : IConsume<DistributedLockReleased>
    {
        public ValueTask Consume(ConsumeContext<DistributedLockReleased> context, CancellationToken cancellationToken)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                return ValueTask.FromCanceled(cancellationToken);
            }

            logger.LogProcessingLockReleased(context.MessageId, context.Message.Resource);

            // Routed through ICanReceiveLockReleased so an IDistributedLockProvider decorator
            // doesn't break the wake-up signal (the registered DistributedLockProvider instance
            // implements the marker directly).
            receiver.OnLockReleased(context.Message);

            return ValueTask.CompletedTask;
        }
    }

    #endregion
}
