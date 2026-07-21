// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Security.Cryptography;
using Headless.Checks;
using Headless.DistributedLocks;
using Headless.Messaging.CircuitBreaker;
using Headless.Messaging.Configuration;
using Headless.Messaging.Internal;
using Headless.Messaging.Messages;
using Headless.Messaging.Persistence;
using Headless.Messaging.Transport;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Headless.Messaging.Processor;

/// <summary>
/// Processes messages that need to be retried, with adaptive polling and circuit breaker awareness.
/// Dashboards and observability extensions resolve it through <see cref="IRetryProcessorMonitor"/>
/// rather than depending on this runtime implementation.
/// </summary>
internal sealed class MessageNeedToRetryProcessor : IProcessor, IRetryProcessorMonitor
{
    private readonly ILogger<MessageNeedToRetryProcessor> _logger;
    private readonly IDispatcher _dispatcher;
    private readonly TimeSpan _baseInterval;
    private readonly TimeSpan _maxInterval;
    private readonly IOptions<MessagingOptions> _options;
    private readonly ICircuitBreakerMonitor? _circuitBreakerMonitor;
    private readonly bool _adaptivePolling;
    private readonly double _circuitOpenRateThreshold;
    private readonly Dictionary<RetryQuadrantKey, RetryQuadrantState> _quadrants;
    private readonly RetryQuadrantState[] _quadrantStates;

    private const int _StoragePickupErrorEscalationThreshold = 3;

    public MessageNeedToRetryProcessor(
        IOptions<MessagingOptions> options,
        IOptions<RetryProcessorOptions> retryOptions,
        ILogger<MessageNeedToRetryProcessor> logger,
        IDispatcher dispatcher,
        [FromKeyedServices(MessagingKeys.LockProvider)] IDistributedLock lockProvider,
        ICircuitBreakerMonitor? circuitBreakerMonitor = null
    )
    {
        _options = options;
        _logger = logger;
        _dispatcher = dispatcher;
        _baseInterval = retryOptions.Value.BaseInterval;
        LockProvider = lockProvider;
        _circuitBreakerMonitor = circuitBreakerMonitor;

        _adaptivePolling = retryOptions.Value.AdaptivePolling;
        _maxInterval = retryOptions.Value.MaxPollingInterval;
        _circuitOpenRateThreshold = retryOptions.Value.CircuitOpenRateThreshold;

        _quadrantStates =
        [
            _CreateState(MessageType.Publish, MessageLane.Bus),
            _CreateState(MessageType.Publish, MessageLane.Queue),
            _CreateState(MessageType.Subscribe, MessageLane.Bus),
            _CreateState(MessageType.Subscribe, MessageLane.Queue),
        ];
        _quadrants = _quadrantStates.ToDictionary(state => state.Key);
    }

    /// <inheritdoc />
    public TimeSpan CurrentPollingInterval => _quadrantStates.Max(state => state.CurrentInterval);

    /// <inheritdoc />
    public bool IsBackedOff => _quadrantStates.Any(state => state.CurrentInterval > _baseInterval);

    /// <summary>The keyed-DI lock provider that was injected. Internal accessor — production code uses this; tests verify injection via InternalsVisibleTo.</summary>
    internal IDistributedLock LockProvider { get; }

    /// <summary>Sets the current polling interval. Exposed for testing via InternalsVisibleTo.</summary>
    internal void SetCurrentIntervalForTest(TimeSpan value)
    {
        SetCurrentIntervalForTest(MessageType.Subscribe, MessageLane.Bus, value);
    }

    internal void SetCurrentIntervalForTest(MessageType direction, MessageLane lane, TimeSpan value)
    {
        var state = _GetState(direction, lane);
        Interlocked.Exchange(ref state._currentIntervalTicks, value.Ticks);
    }

    internal TimeSpan GetCurrentIntervalForTest(MessageType direction, MessageLane lane)
    {
        return _GetState(direction, lane).CurrentInterval;
    }

    internal int GetPickupFailureCountForTest(MessageType direction, MessageLane lane)
    {
        return Volatile.Read(ref _GetState(direction, lane)._consecutivePickupFailures);
    }

    internal async Task WaitForQuadrantIdleForTestAsync(MessageType direction, MessageLane lane)
    {
        var state = _GetState(direction, lane);
        while (state.ActiveTask is { } task)
        {
            await task.ConfigureAwait(false);
        }
    }

    internal void MarkQuadrantDueForTest(MessageType direction, MessageLane lane)
    {
        _GetState(direction, lane).MarkDue();
    }

    internal TimeSpan GetQuadrantDelayForTest(MessageType direction, MessageLane lane, DateTimeOffset now)
    {
        return _GetState(direction, lane).GetDelay(now);
    }

    internal void SetQuadrantActiveTaskForTest(MessageType direction, MessageLane lane, Task task)
    {
        _GetState(direction, lane).ActiveTask = task;
    }

    /// <summary>One-shot flag set after the startup jitter delay fires on the first poll.</summary>
    /// <remarks>
    /// The first <see cref="ProcessAsync"/> call waits a random fraction of <see cref="_baseInterval"/>
    /// before performing any work, so that replicas booting simultaneously do not synchronize their
    /// poll ticks and overwhelm the storage layer (poll-tick storm). Subsequent polls use the
    /// configured interval. Mutated only by <see cref="ProcessAsync"/>, which is invoked sequentially
    /// per processor instance — a plain bool is sufficient.
    /// </remarks>
    internal bool StartupJitterApplied { get; private set; }

    /// <inheritdoc />
    public ValueTask ResetBackpressureAsync(CancellationToken ct = default)
    {
        foreach (var state in _quadrantStates)
        {
            state.Reset(_baseInterval);
        }

        return ValueTask.CompletedTask;
    }

    public async Task ProcessAsync(ProcessingContext context)
    {
        Argument.IsNotNull(context);

        if (!StartupJitterApplied)
        {
            var jitter = TimeSpan.FromTicks((long)(_baseInterval.Ticks * _GetRandomUnitDouble()));
            await context.WaitAsync(jitter).ConfigureAwait(false);
            StartupJitterApplied = true;
        }

        var storage = context.Provider.GetRequiredService<IDataStorage>();
        var startedThisTurn = new HashSet<RetryQuadrantKey>();
        var now = context.GetUtcNow();
        _StartDueQuadrants(storage, context, now, startedThisTurn);

        var wait = _quadrantStates.Min(state => state.GetDelay(context.GetUtcNow()));
        await context.WaitAsync(wait).ConfigureAwait(false);

        // A caller can enter just before one or more quadrants become due. Start those lanes
        // after the wait, but never start a second cycle for a quadrant already run this turn.
        _StartDueQuadrants(storage, context, context.GetUtcNow(), startedThisTurn);
    }

    private void _StartDueQuadrants(
        IDataStorage storage,
        ProcessingContext context,
        DateTimeOffset now,
        ISet<RetryQuadrantKey> startedThisTurn
    )
    {
        foreach (var state in _quadrantStates)
        {
            if (startedThisTurn.Contains(state.Key) || state.ActiveTask is { IsCompleted: false } || !state.IsDue(now))
            {
                continue;
            }

            startedThisTurn.Add(state.Key);
            state.ScheduleNext(now);
            var task = Task
                .Factory.StartNew(
                    () => _ProcessQuadrantAsync(state, storage, context),
                    CancellationToken.None,
                    TaskCreationOptions.DenyChildAttach,
                    TaskScheduler.Default
                )
                .Unwrap();
            state.ActiveTask = task;

            _ = task.ContinueWith(
                completed =>
                {
                    if (completed.IsFaulted)
                    {
                        if (state.Key.Direction == MessageType.Publish)
                        {
                            _logger.PublishedRetryProcessingUnhandled(completed.Exception);
                        }
                        else
                        {
                            _logger.ReceivedRetryProcessingUnhandled(completed.Exception);
                        }
                    }

                    state.ClearActiveTask(completed);
                },
                CancellationToken.None,
                TaskContinuationOptions.ExecuteSynchronously,
                TaskScheduler.Default
            );
        }
    }

    private async Task _ProcessQuadrantAsync(
        RetryQuadrantState state,
        IDataStorage connection,
        ProcessingContext context
    )
    {
        context.ThrowIfStopping();

        if (!_options.Value.UseStorageLock)
        {
            await _ExecuteWorkAsync(state, connection, context).ConfigureAwait(false);
            return;
        }

        await using var acquiredHandle = await _TryAcquireLockAsync(state, context).ConfigureAwait(false);
        if (acquiredHandle is null || _IsLeaseAlreadyLost(state, acquiredHandle))
        {
            return;
        }

        await using var lossRegistration = _RegisterLeaseLossLogger(state, acquiredHandle);
        await _ExecuteWorkAsync(state, connection, context).ConfigureAwait(false);
    }

    private bool _IsLeaseAlreadyLost(RetryQuadrantState state, IDistributedLease lease)
    {
        if (!lease.CanObserveLoss || !lease.LostToken.IsCancellationRequested)
        {
            return false;
        }

        _logger.RetryLockLeaseLost(state.DisplayName, lease.Resource, lease.LeaseId);
        return true;
    }

    private CancellationTokenRegistration _RegisterLeaseLossLogger(RetryQuadrantState state, IDistributedLease lease)
    {
        if (!lease.CanObserveLoss)
        {
            return default;
        }

        return lease.LostToken.Register(
            static state =>
            {
                var (logger, retryKind, resource, leaseId) = ((
                    ILogger<MessageNeedToRetryProcessor>,
                    string,
                    string,
                    string
                ))
                    state!;
                logger.RetryLockLeaseLost(retryKind, resource, leaseId);
            },
            (_logger, state.DisplayName, lease.Resource, lease.LeaseId)
        );
    }

    /// <summary>
    /// Attempts to acquire the published-retry or received-retry distributed lock, wrapping
    /// <c>IDistributedLock.TryAcquireAsync</c> in a lock-specific per-kind escalation-counter pattern
    /// so adaptive polling backs off on lock-store outages rather than tight-looping.
    /// </summary>
    private async Task<IDistributedLease?> _TryAcquireLockAsync(RetryQuadrantState state, ProcessingContext context)
    {
        try
        {
            var lease = await LockProvider
                .TryAcquireAsync(
                    state.LockResource,
                    new DistributedLockAcquireOptions
                    {
                        TimeUntilExpires = state.CurrentInterval,
                        AcquireTimeout = TimeSpan.Zero,
                        Monitoring = LockMonitoringMode.AutoExtend,
                    },
                    context.CancellationToken
                )
                .ConfigureAwait(false);

            if (lease is not null)
            {
                Interlocked.Exchange(ref state._consecutiveLockAcquireFailures, 0);
            }

            return lease;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _RecordLockAcquireFailure(state, ex);
            return null;
        }
    }

    private Task _ExecuteWorkAsync(RetryQuadrantState state, IDataStorage connection, ProcessingContext context)
    {
        return state.Key.Direction switch
        {
            MessageType.Publish => _ExecutePublishedWorkAsync(state, connection, context),
            MessageType.Subscribe => _ExecuteReceivedWorkAsync(state, connection, context),
            _ => throw new InvalidOperationException($"Unsupported retry direction '{state.Key.Direction}'."),
        };
    }

    private async Task _ExecutePublishedWorkAsync(
        RetryQuadrantState state,
        IDataStorage connection,
        ProcessingContext context
    )
    {
        var pickup = await _GetSafelyAsync(
                token => connection.GetPublishedMessagesOfNeedRetryAsync(state.Key.Lane, token),
                state,
                context.CancellationToken
            )
            .ConfigureAwait(false);

        if (!pickup.Succeeded)
        {
            return;
        }

        var enqueued = 0;
        foreach (var message in pickup.Messages)
        {
            context.ThrowIfStopping();

            var persistedLane = message.Lane;
            if (persistedLane != state.Key.Lane)
            {
                throw new InvalidOperationException(
                    $"Retry pickup for lane '{state.Key.Lane}' returned persisted lane '{persistedLane}'."
                );
            }

            await _dispatcher.EnqueueToPublish(message, context.CancellationToken).ConfigureAwait(false);
            enqueued++;
        }

        if (_adaptivePolling)
        {
            _AdjustPollingInterval(state, enqueued, skippedCircuitOpen: 0);
        }
    }

    private async Task _ExecuteReceivedWorkAsync(
        RetryQuadrantState state,
        IDataStorage connection,
        ProcessingContext context
    )
    {
        var pickup = await _GetSafelyAsync(
                token => connection.GetReceivedMessagesOfNeedRetryAsync(state.Key.Lane, token),
                state,
                context.CancellationToken
            )
            .ConfigureAwait(false);

        if (!pickup.Succeeded)
        {
            return;
        }

        var enqueued = 0;
        var skippedCircuitOpen = 0;
        var circuitOpenCache = new Dictionary<string, bool>(StringComparer.Ordinal);

        foreach (var message in pickup.Messages)
        {
            context.ThrowIfStopping();

            var group = message.Origin.GetGroup();
            var persistedLane = message.Lane;
            if (persistedLane != state.Key.Lane)
            {
                throw new InvalidOperationException(
                    $"Retry pickup for lane '{state.Key.Lane}' returned persisted lane '{persistedLane}'."
                );
            }

            if (group is not null && _IsCircuitOpen(state.Key.Lane, group, circuitOpenCache))
            {
                skippedCircuitOpen++;
                var safeGroup = LogSanitizer.Sanitize(group);
                _logger.RetrySkippedBecauseCircuitOpen(message.StorageId, safeGroup);
                continue;
            }

            await _dispatcher
                .EnqueueToExecute(message, descriptor: null, context.CancellationToken)
                .ConfigureAwait(false);

            enqueued++;
        }

        if (_adaptivePolling)
        {
            _AdjustPollingInterval(state, enqueued, skippedCircuitOpen);
        }
    }

    private async Task<RetryPickupResult<T>> _GetSafelyAsync<T>(
        Func<CancellationToken, ValueTask<IEnumerable<T>>> getMessagesAsync,
        RetryQuadrantState state,
        CancellationToken cancellationToken = default
    )
    {
        try
        {
            var result = await getMessagesAsync(cancellationToken).ConfigureAwait(false);
            Interlocked.Exchange(ref state._consecutivePickupFailures, 0);
            return new RetryPickupResult<T>(result, Succeeded: true);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            // Back off polling so a sustained storage outage isn't masked by artificially "clean"
            // cycles (empty list ≠ healthy). Escalate the log to Error after a small streak so
            // monitoring picks up persistent failures.
            var failureCount = Interlocked.Increment(ref state._consecutivePickupFailures);
            Interlocked.Exchange(ref state._consecutiveCleanCycles, 0);
            Interlocked.Exchange(ref state._consecutiveHealthyCycles, 0);
            _CompareExchangeDouble(state);

            if (failureCount >= _StoragePickupErrorEscalationThreshold)
            {
                _logger.RetryStoragePickupFailureEscalated(ex, failureCount);
            }
            else
            {
                _logger.GetMessagesFromStorageFailed(ex);
            }

            return new RetryPickupResult<T>([], Succeeded: false);
        }
    }

    private readonly record struct RetryPickupResult<T>(IEnumerable<T> Messages, bool Succeeded);

    /// <summary>
    /// Records a lock-acquire failure on a lock-specific per-kind counter so adaptive polling backs off
    /// rather than tight-looping a sick lock store. Escalates the log to Error after the same
    /// _StoragePickupErrorEscalationThreshold streak so monitoring sees persistent lock-store outages.
    /// </summary>
    private void _RecordLockAcquireFailure(RetryQuadrantState state, Exception ex)
    {
        var failureCount = Interlocked.Increment(ref state._consecutiveLockAcquireFailures);
        Interlocked.Exchange(ref state._consecutiveCleanCycles, 0);
        Interlocked.Exchange(ref state._consecutiveHealthyCycles, 0);
        _CompareExchangeDouble(state);

        switch (state.Key.Direction)
        {
            case MessageType.Publish:
                if (failureCount >= _StoragePickupErrorEscalationThreshold)
                {
                    _logger.PublishedRetryLockAcquireFailureEscalated(ex, failureCount);
                }
                else
                {
                    _logger.PublishedRetryLockAcquireFailed(ex);
                }
                break;
            case MessageType.Subscribe:
                if (failureCount >= _StoragePickupErrorEscalationThreshold)
                {
                    _logger.ReceivedRetryLockAcquireFailureEscalated(ex, failureCount);
                }
                else
                {
                    _logger.ReceivedRetryLockAcquireFailed(ex);
                }
                break;
            default:
                throw new InvalidOperationException($"Unsupported retry direction '{state.Key.Direction}'.");
        }
    }

    /// <summary>
    /// <para>
    /// Two-counter adaptive polling:
    /// - _consecutiveHealthyCycles: cycles with zero circuit-open messages → halves interval at >=2.
    /// - _consecutiveCleanCycles: cycles with zero total retry messages → resets to base at >=3.
    /// Clean cycles (total==0) increment both counters; the >=3 reset check runs before the >=2
    /// halving check, so a sustained quiet period snaps back to base rather than halving stepwise.
    /// </para>
    /// <para>
    /// All mutations of _currentIntervalTicks use CAS (CompareExchange) loops to avoid
    /// non-atomic read-modify-write races with concurrent ResetBackpressureAsync calls.
    /// </para>
    /// </summary>
    internal void AdjustPollingInterval(int enqueued, int skippedCircuitOpen)
    {
        AdjustPollingInterval(MessageType.Subscribe, MessageLane.Bus, enqueued, skippedCircuitOpen);
    }

    internal void AdjustPollingInterval(MessageType direction, MessageLane lane, int enqueued, int skippedCircuitOpen)
    {
        _AdjustPollingInterval(_GetState(direction, lane), enqueued, skippedCircuitOpen);
    }

    private void _AdjustPollingInterval(RetryQuadrantState state, int enqueued, int skippedCircuitOpen)
    {
        var total = enqueued + skippedCircuitOpen;

        // No messages at all — clean cycle.
        // Zero messages means both "healthy" (no circuit-open skips) and "clean" (no retries
        // pending), so both counters are incremented. The _consecutiveCleanCycles >= 3 check
        // resets to base interval before _consecutiveHealthyCycles >= 2 would halve, giving
        // a full reset priority over gradual step-down when the system is completely idle.
        if (total == 0)
        {
            Interlocked.Increment(ref state._consecutiveCleanCycles);
            Interlocked.Increment(ref state._consecutiveHealthyCycles);

            if (state._consecutiveCleanCycles >= 3)
            {
                Interlocked.Exchange(ref state._currentIntervalTicks, _baseInterval.Ticks);
                Interlocked.Exchange(ref state._consecutiveCleanCycles, 0);
                Interlocked.Exchange(ref state._consecutiveHealthyCycles, 0);
            }
            else if (state._consecutiveHealthyCycles >= 2)
            {
                _CompareExchangeHalve(state);
            }

            return;
        }

        var circuitOpenSkipRate = (double)skippedCircuitOpen / total;

        if (circuitOpenSkipRate > _circuitOpenRateThreshold)
        {
            // High circuit-open rate — back off
            Interlocked.Exchange(ref state._consecutiveHealthyCycles, 0);
            Interlocked.Exchange(ref state._consecutiveCleanCycles, 0);

            _CompareExchangeDouble(state);
        }
        else if (circuitOpenSkipRate <= _circuitOpenRateThreshold / 2.0)
        {
            // Healthy cycle — well below backoff threshold
            Interlocked.Increment(ref state._consecutiveHealthyCycles);
            Interlocked.Exchange(ref state._consecutiveCleanCycles, 0);

            if (state._consecutiveHealthyCycles >= 2)
            {
                _CompareExchangeHalve(state);
                Interlocked.Exchange(ref state._consecutiveHealthyCycles, 0);
            }
        }
        else
        {
            // Between backoff threshold and recovery threshold — hold steady
            Interlocked.Exchange(ref state._consecutiveHealthyCycles, 0);
            Interlocked.Exchange(ref state._consecutiveCleanCycles, 0);
        }
    }

    /// <summary>
    /// CAS loop: doubles _currentIntervalTicks, capped at _maxInterval.
    /// If a concurrent ResetBackpressureAsync modifies the value between read and write,
    /// the loop retries with the fresh value. Logs after successful CAS.
    /// </summary>
    private void _CompareExchangeDouble(RetryQuadrantState state)
    {
        long snapshot;
        long desired;
        do
        {
            snapshot = Interlocked.Read(ref state._currentIntervalTicks);
            desired = snapshot <= _maxInterval.Ticks / 2 ? snapshot * 2 : _maxInterval.Ticks;
        } while (Interlocked.CompareExchange(ref state._currentIntervalTicks, desired, snapshot) != snapshot);

        var increasedInterval = TimeSpan.FromTicks(desired);
        _logger.AdaptivePollingIntervalIncreased(increasedInterval);
    }

    /// <summary>
    /// CAS loop: halves _currentIntervalTicks, floored at _baseInterval.
    /// No-op if already at base interval. Logs after successful CAS.
    /// </summary>
    private void _CompareExchangeHalve(RetryQuadrantState state)
    {
        long snapshot;
        long desired;
        do
        {
            snapshot = Interlocked.Read(ref state._currentIntervalTicks);
            if (snapshot <= _baseInterval.Ticks)
            {
                return; // already at base — nothing to halve
            }

            desired = Math.Max(snapshot / 2, _baseInterval.Ticks);
        } while (Interlocked.CompareExchange(ref state._currentIntervalTicks, desired, snapshot) != snapshot);

        var decreasedInterval = TimeSpan.FromTicks(desired);
        _logger.AdaptivePollingIntervalDecreased(decreasedInterval);
    }

    private bool _IsCircuitOpen(MessageLane lane, string group, Dictionary<string, bool> cache)
    {
        var circuitBreakerGroup = CircuitBreakerGroupKeys.For(lane, group);

        if (cache.TryGetValue(circuitBreakerGroup, out var isOpen))
        {
            return isOpen;
        }

        isOpen = _circuitBreakerMonitor?.IsOpen(circuitBreakerGroup) == true;
        cache[circuitBreakerGroup] = isOpen;
        return isOpen;
    }

    private RetryQuadrantState _CreateState(MessageType direction, MessageLane lane)
    {
        var resource = direction switch
        {
            MessageType.Publish => MessagingKeys.PublishRetryResource(_options.Value.Version, lane),
            MessageType.Subscribe => MessagingKeys.ReceiveRetryResource(_options.Value.Version, lane),
            _ => throw new InvalidOperationException($"Unsupported retry direction '{direction}'."),
        };

        return new RetryQuadrantState(new RetryQuadrantKey(direction, lane), resource, _baseInterval);
    }

    private RetryQuadrantState _GetState(MessageType direction, MessageLane lane)
    {
        var key = new RetryQuadrantKey(direction, lane);
        return _quadrants.TryGetValue(key, out var state)
            ? state
            : throw new InvalidOperationException(
                string.Create(CultureInfo.InvariantCulture, $"Unsupported retry quadrant '{direction}/{(short)lane}'.")
            );
    }

    private sealed record RetryQuadrantKey(MessageType Direction, MessageLane Lane);

#pragma warning disable IDE1006, IDE0032 // Atomic state fields follow the processor's private-field convention.
    private sealed class RetryQuadrantState(RetryQuadrantKey key, string lockResource, TimeSpan baseInterval)
    {
        private long _nextPollUtcTicks;
        private Task? _activeTask;

        internal long _currentIntervalTicks = baseInterval.Ticks;
        internal int _consecutiveHealthyCycles;
        internal int _consecutiveCleanCycles;
        internal int _consecutivePickupFailures;
        internal int _consecutiveLockAcquireFailures;

        public RetryQuadrantKey Key { get; } = key;
        public string LockResource { get; } = lockResource;
        public string DisplayName => $"{Key.Direction}-{Key.Lane}";
        public TimeSpan CurrentInterval => TimeSpan.FromTicks(Interlocked.Read(ref _currentIntervalTicks));

        public Task? ActiveTask
        {
            get => Volatile.Read(ref _activeTask);
            set => Volatile.Write(ref _activeTask, value);
        }

        public void ClearActiveTask(Task completed)
        {
            _ = Interlocked.CompareExchange(ref _activeTask, value: null, completed);
        }

        public bool IsDue(DateTimeOffset now)
        {
            var next = Interlocked.Read(ref _nextPollUtcTicks);
            return next == 0 || now.UtcDateTime.Ticks >= next;
        }

        public void ScheduleNext(DateTimeOffset now)
        {
            var next = now.Add(CurrentInterval).UtcDateTime.Ticks;
            Interlocked.Exchange(ref _nextPollUtcTicks, next);
        }

        public TimeSpan GetDelay(DateTimeOffset now)
        {
            var remaining = Interlocked.Read(ref _nextPollUtcTicks) - now.UtcDateTime.Ticks;
            if (remaining > 0)
            {
                return TimeSpan.FromTicks(remaining);
            }

            // An in-flight pickup can outlive its scheduled cadence. Returning zero here would make
            // InfiniteRetryProcessor re-enter this processor continuously until the task completes.
            return ActiveTask is { IsCompleted: false } ? CurrentInterval : TimeSpan.Zero;
        }

        public void MarkDue()
        {
            Interlocked.Exchange(ref _nextPollUtcTicks, 0);
        }

        public void Reset(TimeSpan basePollingInterval)
        {
            Interlocked.Exchange(ref _currentIntervalTicks, basePollingInterval.Ticks);
            Interlocked.Exchange(ref _consecutiveHealthyCycles, 0);
            Interlocked.Exchange(ref _consecutiveCleanCycles, 0);
            Interlocked.Exchange(ref _consecutivePickupFailures, 0);
            Interlocked.Exchange(ref _consecutiveLockAcquireFailures, 0);
            Interlocked.Exchange(ref _nextPollUtcTicks, 0);
        }
    }
#pragma warning restore IDE1006, IDE0032

    private static double _GetRandomUnitDouble()
    {
        return RandomNumberGenerator.GetInt32(int.MaxValue) / (double)int.MaxValue;
    }
}

internal static partial class RetryProcessorLog
{
    [LoggerMessage(
        EventId = 3107,
        Level = LogLevel.Error,
        Message = "Unhandled exception in published-message retry processing"
    )]
    public static partial void PublishedRetryProcessingUnhandled(this ILogger logger, Exception? ex);

    [LoggerMessage(
        EventId = 3108,
        Level = LogLevel.Error,
        Message = "Unhandled exception in received-message retry processing"
    )]
    public static partial void ReceivedRetryProcessingUnhandled(this ILogger logger, Exception? ex);

    [LoggerMessage(
        EventId = 3109,
        Level = LogLevel.Debug,
        Message = "Skipping retry for message {StorageId} — circuit open for group {Group}"
    )]
    public static partial void RetrySkippedBecauseCircuitOpen(this ILogger logger, Guid storageId, string? group);

    [LoggerMessage(EventId = 3110, Level = LogLevel.Warning, Message = "Get messages from storage failed. Retrying...")]
    public static partial void GetMessagesFromStorageFailed(this ILogger logger, Exception ex);

    [LoggerMessage(
        EventId = 3111,
        Level = LogLevel.Debug,
        Message = "Adaptive polling: circuit-open rate exceeds threshold, interval increased to {Interval}"
    )]
    public static partial void AdaptivePollingIntervalIncreased(this ILogger logger, TimeSpan interval);

    [LoggerMessage(
        EventId = 3112,
        Level = LogLevel.Debug,
        Message = "Adaptive polling: healthy for 2 cycles, interval decreased to {Interval}"
    )]
    public static partial void AdaptivePollingIntervalDecreased(this ILogger logger, TimeSpan interval);
}
