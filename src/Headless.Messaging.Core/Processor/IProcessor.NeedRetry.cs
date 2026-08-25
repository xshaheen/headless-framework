// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Collections.Concurrent;
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
    private readonly ICircuitBreakerStateManager? _circuitBreakerStateManager;
    private readonly bool _adaptivePolling;
    private readonly double _circuitOpenRateThreshold;
    private readonly Dictionary<RetryQuadrantKey, RetryQuadrantState> _quadrants;
    private readonly RetryQuadrantState[] _quadrantStates;
    private readonly ConcurrentDictionary<Type, byte> _unsupportedCircuitDeferralProviders = new();
    private readonly Lock _pickupGate = new();
    private bool _acceptingPickup = true;

    private const int _StoragePickupErrorEscalationThreshold = 3;

    public MessageNeedToRetryProcessor(
        IOptions<MessagingOptions> options,
        IOptions<RetryProcessorOptions> retryOptions,
        ILogger<MessageNeedToRetryProcessor> logger,
        IDispatcher dispatcher,
        [FromKeyedServices(MessagingKeys.LockProvider)] IDistributedLock lockProvider,
        ICircuitBreakerMonitor? circuitBreakerMonitor = null,
        ICircuitBreakerStateManager? circuitBreakerStateManager = null
    )
    {
        _options = options;
        _logger = logger;
        _dispatcher = dispatcher;
        _baseInterval = retryOptions.Value.BaseInterval;
        LockProvider = lockProvider;
        _circuitBreakerMonitor = circuitBreakerMonitor;
        _circuitBreakerStateManager = circuitBreakerStateManager;

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

    internal void StartRun()
    {
        lock (_pickupGate)
        {
            _acceptingPickup = true;
        }
    }

    internal IReadOnlyList<Task> Quiesce()
    {
        lock (_pickupGate)
        {
            _acceptingPickup = false;
            return _quadrantStates
                .Select(static state => state.ActiveTask)
                .Where(static task => task is not null)
                .Cast<Task>()
                .ToArray();
        }
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

        lock (_pickupGate)
        {
            if (!_acceptingPickup)
            {
                return;
            }
        }

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
        lock (_pickupGate)
        {
            if (!_acceptingPickup)
            {
                return;
            }

            foreach (var state in _quadrantStates)
            {
                if (
                    startedThisTurn.Contains(state.Key)
                    || state.ActiveTask is { IsCompleted: false }
                    || !state.IsDue(now)
                )
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

        var messages = pickup.Messages.ToList();
        var enqueued = 0;
        var nextUnhanded = 0;
        try
        {
            while (nextUnhanded < messages.Count)
            {
                context.ThrowIfStopping();

                var message = messages[nextUnhanded];
                var persistedLane = message.Lane;
                if (persistedLane != state.Key.Lane)
                {
                    throw new InvalidOperationException(
                        $"Retry pickup for lane '{state.Key.Lane}' returned persisted lane '{persistedLane}'."
                    );
                }

                if (_dispatcher is IRetryDispatcher retryDispatcher)
                {
                    await retryDispatcher
                        .DispatchPublishedAsync(message, context.CancellationToken)
                        .ConfigureAwait(false);
                }
                else
                {
                    await _dispatcher.EnqueueToPublish(message, context.CancellationToken).ConfigureAwait(false);
                }

                nextUnhanded++;
                enqueued++;
            }
        }
        finally
        {
            await _ReleaseUnhandedAsync(connection, MessageType.Publish, messages, nextUnhanded).ConfigureAwait(false);
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

        var messages = pickup.Messages.ToList();
        var enqueued = 0;
        var skippedCircuitOpen = 0;
        var healthy = new List<MediumMessage>(messages.Count);
        var circuitWork = new List<CircuitRetryWork>();

        foreach (var message in messages)
        {
            var persistedLane = message.Lane;
            if (persistedLane != state.Key.Lane)
            {
                throw new InvalidOperationException(
                    $"Retry pickup for lane '{state.Key.Lane}' returned persisted lane '{persistedLane}'."
                );
            }

            var group = message.Origin.GetGroup();
            var decision = group is null
                ? CircuitRetryDecision.Closed
                : _GetCircuitRetryDecision(state.Key.Lane, group);
            if (decision.Kind is CircuitRetryDecisionKind.Closed)
            {
                healthy.Add(message);
                continue;
            }

            skippedCircuitOpen++;
            if (_logger.IsEnabled(LogLevel.Debug))
            {
                _logger.RetrySkippedBecauseCircuitOpen(message.StorageId, LogSanitizer.Sanitize(group));
            }
            circuitWork.Add(new CircuitRetryWork(message, group!, decision));
        }

        // Circuit claims are deliberately absent from this generic release range. Once classified,
        // only their dedicated exact deferral/probe-generation path may clear the lease.
        var pendingProbeKeys = circuitWork
            .Where(static work => work.Decision.Kind is CircuitRetryDecisionKind.ProbeAcquired)
            .Select(work => CircuitBreakerGroupKeys.For(state.Key.Lane, work.Group))
            .ToHashSet(StringComparer.Ordinal);
        var nextUnhanded = 0;
        try
        {
            try
            {
                while (nextUnhanded < healthy.Count)
                {
                    context.ThrowIfStopping();

                    var message = healthy[nextUnhanded];

                    var transferred = await _DispatchReceivedAsync(message, context.CancellationToken)
                        .ConfigureAwait(false);

                    nextUnhanded++;
                    if (transferred)
                    {
                        enqueued++;
                    }
                }
            }
            finally
            {
                await _ReleaseUnhandedAsync(connection, MessageType.Subscribe, healthy, nextUnhanded)
                    .ConfigureAwait(false);
            }

            // Start the one acquired probe before waiting on siblings that joined its generation.
            foreach (
                var work in circuitWork.Where(static work =>
                    work.Decision.Kind is CircuitRetryDecisionKind.ProbeAcquired
                )
            )
            {
                var probeKey = CircuitBreakerGroupKeys.For(state.Key.Lane, work.Group);
                var transferred = false;
                try
                {
                    transferred = await _DispatchReceivedAsync(work.Message, context.CancellationToken)
                        .ConfigureAwait(false);
                    if (transferred)
                    {
                        pendingProbeKeys.Remove(probeKey);
                        enqueued++;
                    }
                }
                catch (Exception ex)
                {
                    _logger.CircuitRetryDispositionFailed(
                        ex,
                        work.Message.StorageId,
                        LogSanitizer.Sanitize(work.Group)
                    );
                }

                if (!transferred && pendingProbeKeys.Remove(probeKey))
                {
                    _circuitBreakerStateManager?.ReleaseHalfOpenProbe(probeKey);
                }
            }

            foreach (
                var work in circuitWork.Where(static work =>
                    work.Decision.Kind is not CircuitRetryDecisionKind.ProbeAcquired
                )
            )
            {
                await _DisposeCircuitClaimAsync(connection, work, context).ConfigureAwait(false);
            }
        }
        finally
        {
            foreach (var probeKey in pendingProbeKeys)
            {
                _circuitBreakerStateManager?.ReleaseHalfOpenProbe(probeKey);
            }
        }

        if (_adaptivePolling)
        {
            _AdjustPollingInterval(state, enqueued, skippedCircuitOpen);
        }
    }

    private async ValueTask<bool> _DispatchReceivedAsync(MediumMessage message, CancellationToken cancellationToken)
    {
        if (_dispatcher is IRetryDispatcher retryDispatcher)
        {
            return await retryDispatcher.DispatchReceivedAsync(message, cancellationToken).ConfigureAwait(false);
        }

        await _dispatcher.EnqueueToExecute(message, descriptor: null, cancellationToken).ConfigureAwait(false);
        return true;
    }

    private CircuitRetryDecision _GetCircuitRetryDecision(MessageLane lane, string group)
    {
        if (_circuitBreakerStateManager is not null)
        {
            return _circuitBreakerStateManager.GetRetryDecision(lane, group);
        }

        // A read-only monitor cannot reserve the shared probe generation. Conservatively retain
        // an open claim rather than recreating the old clear-without-deferral hot loop.
        return _circuitBreakerMonitor?.IsOpen(CircuitBreakerGroupKeys.For(lane, group)) == true
            ? new CircuitRetryDecision(CircuitRetryDecisionKind.ProbePending, NextProbeAt: null, ProbeOutcome: null)
            : CircuitRetryDecision.Closed;
    }

    private async ValueTask _DisposeCircuitClaimAsync(
        IDataStorage storage,
        CircuitRetryWork work,
        ProcessingContext context
    )
    {
        try
        {
            switch (work.Decision.Kind)
            {
                case CircuitRetryDecisionKind.Defer:
                    await _DeferCircuitClaimAsync(storage, work.Message, work.Decision.NextProbeAt!.Value)
                        .ConfigureAwait(false);
                    break;
                case CircuitRetryDecisionKind.ProbePending when work.Decision.ProbeOutcome is { } outcomeTask:
                    var outcome = await outcomeTask.WaitAsync(context.CancellationToken).ConfigureAwait(false);
                    if (outcome.Kind is CircuitRetryProbeOutcomeKind.Closed)
                    {
                        await _ReleaseClaimedAsync(storage, MessageType.Subscribe, work.Message).ConfigureAwait(false);
                    }
                    else if (outcome.Kind is CircuitRetryProbeOutcomeKind.Reopened)
                    {
                        await _DeferCircuitClaimAsync(storage, work.Message, outcome.NextProbeAt!.Value)
                            .ConfigureAwait(false);
                    }
                    break;
            }
        }
        catch (Exception ex)
        {
            // An exception, cancellation, or unknown provider outcome leaves this exact lease in
            // place. In particular, do not route it through the generic abandoned-claim releaser.
            _logger.CircuitRetryDispositionFailed(ex, work.Message.StorageId, LogSanitizer.Sanitize(work.Group));
        }
    }

    private ValueTask<bool> _DeferCircuitClaimAsync(
        IDataStorage storage,
        MediumMessage message,
        DateTimeOffset nextRetryAt
    )
    {
        if (storage is not ICircuitRetryDeferralStorage deferralStorage)
        {
            if (_unsupportedCircuitDeferralProviders.TryAdd(storage.GetType(), 0))
            {
                _logger.CircuitRetryDeferralUnsupported(storage.GetType().FullName ?? storage.GetType().Name);
            }

            return ValueTask.FromResult(false);
        }

        if (message.LockedUntil is not { } lockedUntil)
        {
            return ValueTask.FromResult(false);
        }

        var identity = new MessageLeaseIdentity(message.StorageId, message.Owner, lockedUntil, message.Lane);
        return deferralStorage.DeferReceivedRetryAsync(
            new CircuitRetryDeferral(identity, nextRetryAt),
            CancellationToken.None
        );
    }

    private readonly record struct CircuitRetryWork(MediumMessage Message, string Group, CircuitRetryDecision Decision);

    private static ValueTask _ReleaseUnhandedAsync(
        IDataStorage storage,
        MessageType direction,
        List<MediumMessage> messages,
        int startIndex
    )
    {
        return RetryDispatchAttempt.ReleaseClaimedBatchAsync(storage, direction, messages.Skip(startIndex));
    }

    private static ValueTask _ReleaseClaimedAsync(IDataStorage storage, MessageType direction, MediumMessage message)
    {
        var attempt = RetryDispatchAttempt.TryCreate(storage, direction, message);
        return attempt?.AbandonClaimedAsync() ?? ValueTask.CompletedTask;
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

    [LoggerMessage(
        EventId = 3119,
        Level = LogLevel.Warning,
        Message = "Circuit retry disposition failed for message {StorageId} in group {Group}; retaining the claimed lease"
    )]
    public static partial void CircuitRetryDispositionFailed(
        this ILogger logger,
        Exception exception,
        Guid storageId,
        string? group
    );

    [LoggerMessage(
        EventId = 3120,
        Level = LogLevel.Warning,
        Message = "Storage provider {Provider} does not support atomic circuit retry deferral; retaining circuit-open leases until expiry"
    )]
    public static partial void CircuitRetryDeferralUnsupported(this ILogger logger, string provider);

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
