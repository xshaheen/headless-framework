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
    private static readonly TimeSpan _OrphanLockCleanupTimeout = TimeSpan.FromSeconds(5);
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
        Argument.IsLessThanOrEqualTo(resource.Length, _maxResourceNameLength);
        cancellationToken.ThrowIfCancellationRequested();

        timeUntilExpires = _NormalizeTimeUntilExpires(timeUntilExpires);
        var lockId = longIdGenerator.Create().ToString(CultureInfo.InvariantCulture);

        logger.LogAttemptingToAcquireLock(resource, lockId);

        using var timeoutCts = timeProvider.CreateCancellationTokenSource(acquireTimeout ?? DefaultAcquireTimeout);
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(timeoutCts.Token, cancellationToken);
        using var activity = _StartLockActivity(resource);

        var timestamp = timeProvider.GetTimestamp();
        var gotLock = false;
        ResetEventWithRefCount? autoResetEvent = null;
        var retryAttempt = 0;

        try
        {
            do
            {
                // Try to acquire the lock
                try
                {
                    gotLock = await _storage.InsertAsync(resource, lockId, timeUntilExpires, cts.Token);
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
