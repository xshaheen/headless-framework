// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Collections.Concurrent;
using System.Diagnostics;
using System.Diagnostics.Metrics;
using Headless.Abstractions;
using Headless.Checks;
using Headless.Constants;
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

    private readonly Counter<int> _lockTimeoutCounter = HeadlessDiagnostics.Meter.CreateCounter<int>(
        "headless.lock.failed",
        description: "Number of failed attempts to acquire a lock"
    );

    private readonly Histogram<double> _lockWaitTimeHistogram = HeadlessDiagnostics.Meter.CreateHistogram<double>(
        "headless.lock.wait.time",
        unit: "ms",
        description: "Time waiting for locks"
    );

    ILogger IHaveLogger.Logger => logger;

    TimeProvider IHaveTimeProvider.TimeProvider => timeProvider;

    private static readonly TimeSpan _LongLockWarningThreshold = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan _MinRetryDelay = TimeSpan.FromMilliseconds(50);
    private static readonly TimeSpan _MaxRetryDelay = TimeSpan.FromSeconds(3);
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
        _ValidateResourceName(resource);
        cancellationToken.ThrowIfCancellationRequested();

        timeUntilExpires = _NormalizeTimeUntilExpires(timeUntilExpires);
        var lockId = longIdGenerator.Create().ToString(CultureInfo.InvariantCulture);

        logger.LogAttemptingToAcquireLock(resource, lockId);

        using var cts = (acquireTimeout ?? DefaultAcquireTimeout).ToCancellationTokenSource(cancellationToken);
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
                    gotLock = await _storage.InsertAsync(resource, lockId, timeUntilExpires);
                }
                catch (Exception e) when (e is not (ObjectDisposedException or InvalidOperationException))
                {
                    // Swallow transient errors (network, timeout) and retry
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

                // Use exponential backoff instead of storage call
                var delayAmount = _GetBackoffDelay(retryAttempt++);
                logger.LogDelayBeforeRetry(resource, lockId, delayAmount);

                // Wait until we get a message saying the lock was released by (autoResetEvent.Target.Set())
                // or delayAmount has elapsed
                // or acquire timeout cancellation has been requested
                using var linkedCancellationTokenSource = delayAmount.ToCancellationTokenSource(cts.Token);
                await autoResetEvent.Target.SafeWaitAsync(linkedCancellationTokenSource.Token).ConfigureAwait(false);
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

        return new DisposableDistributedLock(resource, lockId, timeWaitedForLock, this, timeProvider, logger);
    }

    private void _ValidateResourceName(string resource)
    {
        if (resource.Length > _maxResourceNameLength)
        {
            FormattableString message = $"Resource name exceeds maximum length of {_maxResourceNameLength} characters";
            throw new ArgumentException(message.ToString(CultureInfo.InvariantCulture), nameof(resource));
        }
    }

    private ResetEventWithRefCount _IncrementResetEvent(string resource)
    {
        lock (_resetEventLock)
        {
            if (_autoResetEvents.TryGetValue(resource, out var existing))
            {
                // Prevent unbounded waiters per resource (DoS protection)
                if (_maxWaitersPerResource is { } max && existing.RefCount >= max)
                {
                    FormattableString message = $"Maximum waiters per resource ({max}) exceeded";
                    throw new InvalidOperationException(message.ToString(CultureInfo.InvariantCulture));
                }

                existing.Increment();

                return existing;
            }

            // Prevent unbounded unique resources (DoS protection)
            if (_maxConcurrentWaitingResources is { } maxResources && _autoResetEvents.Count >= maxResources)
            {
                throw new InvalidOperationException($"Maximum concurrent waiting resources ({maxResources}) exceeded");
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
                (_storage, resource, lockId),
                static state =>
                {
                    var (storage, resource, lockId) = state;

                    return storage.RemoveIfEqualAsync(resource, lockId).AsTask();
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
            (_storage, resource, lockId, timeUntilExpires),
            static state =>
            {
                var (storage, resource, lockId, ttl) = state;

                return storage.ReplaceIfEqualAsync(resource, lockId, lockId, ttl).AsTask();
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
            static x => x._storage.ExistsAsync(x.resource).AsTask(),
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
            (_storage, resource),
            static x => x._storage.GetExpirationAsync(x.resource).AsTask(),
            timeProvider: timeProvider,
            cancellationToken: cancellationToken
        );
    }

    public async Task<LockInfo?> GetLockInfoAsync(string resource, CancellationToken cancellationToken = default)
    {
        Argument.IsNotNullOrWhiteSpace(resource);

        var lockId = await Run.WithRetriesAsync(
                (_storage, resource),
                static x => x._storage.GetAsync(x.resource).AsTask(),
                timeProvider: timeProvider,
                cancellationToken: cancellationToken
            )
            .ConfigureAwait(false);

        if (lockId is null)
        {
            return null;
        }

        var ttl = await _storage.GetExpirationAsync(resource).ConfigureAwait(false);

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
                _storage,
                static storage => storage.GetAllByPrefixAsync("").AsTask(),
                timeProvider: timeProvider,
                cancellationToken: cancellationToken
            )
            .ConfigureAwait(false);

        var result = new List<LockInfo>(locks.Count);

        foreach (var (resource, lockId) in locks)
        {
            var ttl = await _storage.GetExpirationAsync(resource).ConfigureAwait(false);
            result.Add(
                new LockInfo
                {
                    Resource = resource,
                    LockId = lockId,
                    TimeToLive = ttl,
                }
            );
        }

        return result;
    }

    public Task<int> GetActiveLocksCountAsync(CancellationToken cancellationToken = default)
    {
        return Run.WithRetriesAsync(
            _storage,
            static storage => storage.GetCountAsync("").AsTask(),
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
        activity.DisplayName = string.Concat("Lock: ", resource);

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

            logger.LogDebug(
                "Processing lock released {MessageId} for {Resource}",
                context.MessageId,
                context.Message.Resource
            );

            if (provider is DistributedLockProvider impl)
            {
                impl._OnLockReleased(context.Message);
            }

            return ValueTask.CompletedTask;
        }
    }

    #endregion
}
