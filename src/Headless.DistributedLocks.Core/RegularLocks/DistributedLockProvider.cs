// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Collections.Concurrent;
using System.Diagnostics;
using Headless.Abstractions;
using Headless.Checks;
using Headless.Core;
using Headless.Messaging;
using Microsoft.Extensions.Logging;
using Nito.AsyncEx;

#pragma warning disable IDE0130
// ReSharper disable once CheckNamespace
namespace Headless.DistributedLocks;

public sealed class DistributedLockProvider(
    IDistributedLockStorage storage,
    IOutboxPublisher outboxPublisher,
    DistributedLockOptions options,
    ILongIdGenerator longIdGenerator,
    TimeProvider timeProvider,
    ILogger<DistributedLockProvider> logger
) : IDistributedLockProvider, IHaveLogger, IHaveTimeProvider
{
    private readonly ScopedDistributedLockStorage _storage = new(storage, options.KeyPrefix);

    private readonly ConcurrentDictionary<string, ResetEventWithRefCount> _autoResetEvents = new(
        StringComparer.Ordinal
    );

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

    private const int _MaxReleaseRetryAttempts = 15;

    // Configurable limits from options
    private readonly int _maxResourceNameLength = options.MaxResourceNameLength;
    private readonly int? _maxConcurrentWaitingResources = options.MaxConcurrentWaitingResources;
    private readonly int? _maxWaitersPerResource = options.MaxWaitersPerResource;

    public TimeSpan DefaultTimeUntilExpires { get; } = TimeSpan.FromMinutes(20);

    public TimeSpan DefaultAcquireTimeout { get; } = TimeSpan.FromSeconds(30);

    #region Acquire

    public async Task<IDistributedLock?> TryAcquireAsync(
        string resource,
        TimeSpan? timeUntilExpires = null,
        TimeSpan? acquireTimeout = null,
        CancellationToken cancellationToken = default
    )
    {
        Argument.IsNotNullOrWhiteSpace(resource);
        Argument.IsLessThanOrEqualTo(resource.Length, _maxResourceNameLength, paramName: nameof(resource));
        cancellationToken.ThrowIfCancellationRequested();

        timeUntilExpires = _NormalizeTimeUntilExpires(timeUntilExpires);
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
            return await _TryAcquireOnceAsync(resource, lockId, timeUntilExpires, timestamp, cancellationToken)
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

        return new DisposableDistributedLock(resource, lockId, timeWaitedForLock, this, timeProvider, logger);
    }

    private async Task<IDistributedLock?> _TryAcquireOnceAsync(
        string resource,
        string lockId,
        TimeSpan? timeUntilExpires,
        long timestamp,
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

        return new DisposableDistributedLock(resource, lockId, timeWaitedForLock, this, timeProvider, logger);
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
        var delayMs = _MinRetryDelay.TotalMilliseconds * Math.Pow(2, attempt);
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

        logger.LogReleaseStarted(resource, lockId);

        var removed = await Run.WithRetriesAsync(
                (_storage, resource, lockId, cancellationToken),
                static state =>
                {
                    var (storage, resource, lockId, ct) = state;

                    return storage.RemoveIfEqualAsync(resource, lockId, ct).AsTask();
                },
                maxAttempts: _MaxReleaseRetryAttempts,
                timeProvider: timeProvider,
                cancellationToken: cancellationToken
            )
            .ConfigureAwait(false);

        // Only publish if we actually removed the lock.
        // Publish notifies waiters immediately; if skipped, waiters retry via backoff.
        if (removed)
        {
            var distributedLockReleased = new DistributedLockReleased(resource, lockId);
            await outboxPublisher
                .PublishAsync(distributedLockReleased, cancellationToken: cancellationToken)
                .ConfigureAwait(false);
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
            (_storage, resource, lockId, timeUntilExpires, cancellationToken),
            static state =>
            {
                var (storage, resource, lockId, ttl, ct) = state;

                return storage.ReplaceIfEqualAsync(resource, lockId, lockId, ttl, ct).AsTask();
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
            (_storage, resource, cancellationToken),
            static x => x._storage.ExistsAsync(x.resource, x.cancellationToken).AsTask(),
            timeProvider: timeProvider,
            cancellationToken: cancellationToken
        );
    }

    #endregion

    #region Observability

    public Task<TimeSpan?> GetExpirationAsync(string resource, CancellationToken cancellationToken = default)
    {
        Argument.IsNotNullOrWhiteSpace(resource);

        return Run.WithRetriesAsync(
            (_storage, resource, cancellationToken),
            static x => x._storage.GetExpirationAsync(x.resource, x.cancellationToken).AsTask(),
            timeProvider: timeProvider,
            cancellationToken: cancellationToken
        );
    }

    public async Task<LockInfo?> GetLockInfoAsync(string resource, CancellationToken cancellationToken = default)
    {
        Argument.IsNotNullOrWhiteSpace(resource);

        var lockId = await Run.WithRetriesAsync(
                (_storage, resource, cancellationToken),
                static x => x._storage.GetAsync(x.resource, x.cancellationToken).AsTask(),
                timeProvider: timeProvider,
                cancellationToken: cancellationToken
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
        var locks = await Run.WithRetriesAsync(
                (_storage, cancellationToken),
                static x => x._storage.GetAllWithExpirationByPrefixAsync("", x.cancellationToken).AsTask(),
                timeProvider: timeProvider,
                cancellationToken: cancellationToken
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
        return Run.WithRetriesAsync(
            (_storage, cancellationToken),
            static x => x._storage.GetCountAsync("", x.cancellationToken).AsTask(),
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
        var activity = DistributedLocksDiagnostics.Start("lock.acquire");

        if (activity is null)
        {
            return null;
        }

        activity.AddTag("headless.lock.resource", resource);
        activity.DisplayName = $"Lock: {resource}";

        return activity;
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

    internal void _OnLockReleased(DistributedLockReleased message)
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
    }

    internal sealed class LockReleasedConsumer(IDistributedLockProvider provider, ILogger<LockReleasedConsumer> logger)
        : IConsume<DistributedLockReleased>
    {
        public ValueTask Consume(ConsumeContext<DistributedLockReleased> context, CancellationToken cancellationToken)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                return ValueTask.FromCanceled(cancellationToken);
            }

            logger.LogProcessingLockReleased(context.MessageId, context.Message.Resource);

            if (provider is DistributedLockProvider impl)
            {
                impl._OnLockReleased(context.Message);
            }

            return ValueTask.CompletedTask;
        }
    }

    #endregion
}
