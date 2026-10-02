// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Collections.Concurrent;
using Headless.Checks;
using Headless.Messaging.Internal;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Headless.Messaging.CircuitBreaker;

/// <summary>
/// Default implementation of <see cref="ICircuitBreakerStateManager"/>.
/// Maintains per-consumer circuit state and drives Open → HalfOpen transitions via <see cref="Timer"/>.
/// Thread safety is achieved with a per-consumer <see cref="Lock"/> object (embedded in
/// <see cref="ConsumerCircuitState"/>) for all compound check-and-transition operations.
/// </summary>
/// <remarks>
/// This is a custom circuit breaker rather than Polly's <c>CircuitBreakerStrategyOptions</c> because
/// Polly operates at the per-call pipeline level and cannot coordinate transport-level pause/resume
/// across a consumer. This implementation provides per-consumer state tracking, escalating open
/// durations, and direct integration with the transport pause/resume lifecycle and OTel metrics.
/// </remarks>
internal sealed class CircuitBreakerStateManager(
    IOptions<CircuitBreakerOptions> options,
    ConsumerCircuitBreakerRegistry registry,
    ILogger<CircuitBreakerStateManager> logger,
    CircuitBreakerMetrics metrics,
    TimeProvider timeProvider
) : ICircuitBreakerStateManager, IAsyncDisposable, IDisposable
{
    // Lock-free reads on per-consumer state are intentional throughout this class:
    //   - ConsumerCircuitState.State and ConsecutiveFailures use Volatile.Read/Write via their
    //     property accessors (see field comments in ConsumerCircuitState).
    //   - _disposed uses Interlocked.Exchange for writes and Volatile.Read for reads.
    // ReSharper's InconsistentlySynchronizedField analyzer flags these because the same fields
    // are also touched inside per-consumer locks — but the lock protects compound state transitions,
    // not single-field visibility, and Volatile/Interlocked provide visibility on their own.
    private readonly CircuitBreakerOptions _options = options.Value;

    private readonly ConcurrentDictionary<string, ConsumerCircuitState> _circuits = new(StringComparer.Ordinal);

    private readonly CancellationTokenSource _disposalCts = new();

    private long _epochCounter;

    private int _disposed;

    /// <summary>
    /// Flag to ensure the cap-reached warning is logged at most once.
    /// 0 = not logged, 1 = logged. Uses <see cref="Interlocked.CompareExchange(ref int, int, int)"/>
    /// for atomic check-and-set.
    /// </summary>
    private int _capWarningLogged;

    /// <summary>
    /// Known consumer names registered at startup. When populated (non-empty),
    /// <see cref="_GetOrAddState"/> returns a static no-op state for unrecognized names
    /// to prevent unbounded OTel cardinality. Empty before <see cref="RegisterKnownConsumers"/> is called.
    /// </summary>
    private FrozenSet<string> _knownConsumers = [];

    /// <summary>
    /// Static no-op state returned for unrecognized consumer names. Permanently Closed,
    /// disabled, with no real tracking.
    /// </summary>
    private static readonly ConsumerCircuitState _NoOpState = new()
    {
        ConsumerKey = "_noop",
        Enabled = false,
        EffectiveFailureThreshold = int.MaxValue,
        EffectiveOpenDuration = TimeSpan.MaxValue,
        EffectiveIsTransient = static _ => false,
    };

    /// <inheritdoc />
    public void RegisterConsumerCallbacks(
        string consumerKey,
        Func<long, ValueTask> onPause,
        Func<long, ValueTask> onResume
    )
    {
        var state = _GetOrAddState(consumerKey);
        var stateLock = state.SyncLock;

        lock (stateLock)
        {
            state.OnPause = onPause;
            state.OnResume = onResume;
        }
    }

    /// <summary>
    /// Freezes the set of valid consumer names. After this call, <see cref="_GetOrAddState"/>
    /// returns a static no-op state for any consumer name not in the set, and metrics tag unrecognized
    /// names as <c>_unknown</c>. Should be called once during startup after all consumers are registered.
    /// </summary>
    public void RegisterKnownConsumers(IEnumerable<string> consumers)
    {
        var frozen = consumers.ToFrozenSet(StringComparer.Ordinal);
        Volatile.Write(ref _knownConsumers, frozen);

        // Pre-populate state for all known consumers so GetAllStates() returns them immediately
        foreach (var consumer in frozen)
        {
            _GetOrAddState(consumer);
        }

        metrics.SetKnownConsumers(frozen);
        metrics.RegisterStateCallback(GetAllStates);
    }

    /// <inheritdoc />
    public async ValueTask ReportFailureAsync(
        string consumerKey,
        Exception exception,
        CancellationToken cancellationToken = default
    )
    {
        var state = _GetOrAddState(consumerKey);

        if (!state.Enabled)
        {
            return;
        }

        bool isTransient;

        try
        {
            isTransient = state.EffectiveIsTransient(exception);
        }
        catch (Exception ex)
        {
            logger.IsTransientPredicateFailed(ex, LogSanitizer.Sanitize(consumerKey));
            isTransient = false;
        }

        var stateLock = state.SyncLock;
        Func<long, ValueTask>? pauseCallback = null;
        var tripped = false;
        var closedFromHalfOpen = false;
        TimeSpan? openDuration = null;
        ITimer? closedTimerToDispose = null;
        ITimer? openTimerToDispose = null;
        (
            CircuitBreakerState PreviousState,
            TimeSpan OpenDuration,
            long Epoch,
            int Failures,
            int Escalation,
            ITimer? OldTimerToDispose
        ) openInfo = default;

        lock (stateLock)
        {
            switch (state.State)
            {
                case CircuitBreakerState.HalfOpen when !isTransient:
                    // Non-transient failure during probe: the message is bad but the dependency is healthy.
                    // Close the circuit so normal processing resumes.
                    state.ProbeAcquired = false;
                    state.ProbeAcquiredEpoch = null;
                    (openDuration, closedTimerToDispose) = _TransitionToClosed(state, probeSucceeded: false);
                    closedFromHalfOpen = true;
                    break;

                case CircuitBreakerState.HalfOpen when isTransient:
                    // Transient failure during probe: dependency still unhealthy — re-open.
                    state.ProbeAcquired = false;
                    state.ProbeAcquiredEpoch = null;
                    openInfo = _TransitionToOpen(state);
                    openTimerToDispose = openInfo.OldTimerToDispose;
                    pauseCallback = state.OnPause;
                    tripped = true;
                    break;

                default:
                    if (!isTransient || state.State is not CircuitBreakerState.Closed)
                    {
                        // Non-transient failure in Closed/Open state: ignore — not a signal for the breaker.
                        // Also ignore transient failures in Open state — consecutive failures are not tracked there.
                        break;
                    }

                    state.ConsecutiveFailures++;

                    if (state.ConsecutiveFailures >= state.EffectiveFailureThreshold)
                    {
                        openInfo = _TransitionToOpen(state);
                        openTimerToDispose = openInfo.OldTimerToDispose;
                        pauseCallback = state.OnPause;
                        tripped = true;
                    }

                    break;
            }
        }

        // Log transitions outside the lock
        if (tripped)
        {
            logger.CircuitOpened(
                openInfo.PreviousState,
                LogSanitizer.Sanitize(consumerKey),
                openInfo.Failures,
                openInfo.Escalation,
                openInfo.OpenDuration
            );
        }
        else if (closedFromHalfOpen)
        {
            logger.CircuitClosedAfterNonTransientHalfOpenFailure(LogSanitizer.Sanitize(consumerKey));
        }

        // Dispose timers outside the lock
        if (closedTimerToDispose is not null)
        {
            await closedTimerToDispose.DisposeAsync().ConfigureAwait(false);
        }

        if (openTimerToDispose is not null)
        {
            await openTimerToDispose.DisposeAsync().ConfigureAwait(false);
        }

        // Create timer and emit metrics outside the lock
        if (tripped)
        {
            await _CreateAndAssignOpenTimer(state, openInfo.OpenDuration, openInfo.Epoch).ConfigureAwait(false);
            metrics.RecordTrip(consumerKey);
        }

        if (openDuration is not null)
        {
            metrics.RecordOpenDuration(consumerKey, openDuration.Value);
        }

        if (pauseCallback is not null)
        {
            await pauseCallback(openInfo.Epoch).ConfigureAwait(false);
        }
    }

    /// <inheritdoc />
    public CircuitRetryDecision GetRetryDecision(MessageLane lane, string consumerIdentity)
    {
        var circuitKey = CircuitBreakerKeys.For(lane, consumerIdentity);
        if (!_circuits.TryGetValue(circuitKey, out var state))
        {
            return CircuitRetryDecision.Closed;
        }

        var stateLock = state.SyncLock;
        Func<long, ValueTask>? resumeCallback = null;
        TaskCompletionSource? resumeTcs = null;
        long resumeEpoch = 0;
        CircuitRetryDecision decision;
        var transitionedToHalfOpen = false;

        lock (stateLock)
        {
            if (state.State is CircuitBreakerState.Closed)
            {
                return CircuitRetryDecision.Closed;
            }

            if (state.State is CircuitBreakerState.Open)
            {
                var remaining = _GetRemainingOpenDuration(state);
                if (remaining > TimeSpan.Zero)
                {
                    return new CircuitRetryDecision(
                        CircuitRetryDecisionKind.Defer,
                        _GetNextProbeAt(state),
                        ProbeOutcome: null,
                        state.CurrentEpoch
                    );
                }

                // The timer callback may be queued but not yet running. Advance the same epoch
                // under the consumer lock so a persisted retry can become the probe without waiting for
                // a fresh broker delivery. The queued callback observes State != Open and exits.
                state.CurrentEpoch = _NextEpoch();
                state.State = CircuitBreakerState.HalfOpen;
                transitionedToHalfOpen = true;
                resumeCallback = state.OnResume;
                if (resumeCallback is not null)
                {
                    resumeEpoch = state.CurrentEpoch;
                    resumeTcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                    state.InFlightResumes.TryAdd(resumeTcs.Task, 0);
                }
            }

            state.RetryProbeOutcome ??= new TaskCompletionSource<CircuitRetryProbeOutcome>(
                TaskCreationOptions.RunContinuationsAsynchronously
            );

            if (!state.ProbeAcquired)
            {
                state.ProbeAcquired = true;
                state.ProbeAcquiredEpoch = state.CurrentEpoch;
                decision = new CircuitRetryDecision(
                    CircuitRetryDecisionKind.ProbeAcquired,
                    NextProbeAt: null,
                    state.RetryProbeOutcome.Task,
                    state.CurrentEpoch
                );
            }
            else
            {
                decision = new CircuitRetryDecision(
                    CircuitRetryDecisionKind.ProbePending,
                    NextProbeAt: null,
                    state.RetryProbeOutcome.Task,
                    state.CurrentEpoch
                );
            }
        }

        if (transitionedToHalfOpen)
        {
            logger.CircuitHalfOpen(circuitKey);
            _StartResumeCallback(state, circuitKey, resumeCallback, resumeEpoch, resumeTcs);
        }

        return decision;
    }

    /// <inheritdoc />
    public long? TryAcquireHalfOpenProbe(string consumerKey)
    {
        if (!_circuits.TryGetValue(consumerKey, out var state))
        {
            return 0;
        }

        var stateLock = state.SyncLock;

        lock (stateLock)
        {
            if (state.State is not CircuitBreakerState.HalfOpen)
            {
                return state.CurrentEpoch;
            }

            if (state.ProbeAcquired)
            {
                return null;
            }

            state.RetryProbeOutcome ??= new TaskCompletionSource<CircuitRetryProbeOutcome>(
                TaskCreationOptions.RunContinuationsAsynchronously
            );
            state.ProbeAcquired = true;
            state.ProbeAcquiredEpoch = state.CurrentEpoch;
            return state.CurrentEpoch;
        }
    }

    /// <inheritdoc />
    public bool TryGetOpenEpoch(string consumerKey, out long epoch)
    {
        Argument.IsNotNull(consumerKey);

        if (!_circuits.TryGetValue(consumerKey, out var state))
        {
            epoch = 0;
            return false;
        }

        var stateLock = state.SyncLock;

        lock (stateLock)
        {
            if (state.State is not CircuitBreakerState.Open)
            {
                epoch = 0;
                return false;
            }

            epoch = state.CurrentEpoch;
            return true;
        }
    }

    /// <inheritdoc />
    public void ReleaseHalfOpenProbe(string consumerKey, long epoch)
    {
        if (!_circuits.TryGetValue(consumerKey, out var state))
        {
            return;
        }

        var stateLock = state.SyncLock;

        lock (stateLock)
        {
            if (state.State is not CircuitBreakerState.HalfOpen || state.ProbeAcquiredEpoch != epoch)
            {
                return;
            }

            state.ProbeAcquired = false;
            state.ProbeAcquiredEpoch = null;
            _CompleteRetryProbeOutcome(state, CircuitRetryProbeOutcomeKind.Uncertain, nextProbeAt: null);
        }
    }

    /// <inheritdoc />
    public async ValueTask ReportSuccessAsync(string consumerKey, CancellationToken cancellationToken = default)
    {
        if (!_circuits.TryGetValue(consumerKey, out var state))
        {
            return;
        }

        // Fast path: skip lock when circuit is Closed and no failures to reset.
        // Both State and ConsecutiveFailures use Volatile reads for ARM64 visibility.
        if (state.State is CircuitBreakerState.Closed && state.ConsecutiveFailures == 0)
        {
            return;
        }

        var stateLock = state.SyncLock;
        TimeSpan? openDuration = null;
        ITimer? closedTimerToDispose = null;
        var transitionedToClosed = false;

        lock (stateLock)
        {
            if (state.State is CircuitBreakerState.Closed)
            {
                state.ConsecutiveFailures = 0;
            }
            else if (state.State is CircuitBreakerState.HalfOpen)
            {
                state.ProbeAcquired = false;
                state.ProbeAcquiredEpoch = null;
                (openDuration, closedTimerToDispose) = _TransitionToClosed(state, probeSucceeded: true);
                transitionedToClosed = true;
            }
            // Open state: do NOT reset ConsecutiveFailures — preserve failure history
            // so the circuit doesn't close prematurely when the timer transitions to HalfOpen.
        }

        if (transitionedToClosed)
        {
            logger.CircuitClosedAfterProbeSucceeded(consumerKey);
        }

        if (closedTimerToDispose is not null)
        {
            await closedTimerToDispose.DisposeAsync().ConfigureAwait(false);
        }

        if (openDuration is not null)
        {
            metrics.RecordOpenDuration(consumerKey, openDuration.Value);
        }
    }

    /// <inheritdoc />
    public bool IsOpen(string consumerKey)
    {
        Argument.IsNotNull(consumerKey);
        Argument.IsLessThanOrEqualTo(consumerKey.Length, 256);

        if (!_circuits.TryGetValue(consumerKey, out var state))
        {
            return false;
        }

        // No lock needed — State property uses Volatile.Read for cross-thread visibility
        return state.State is CircuitBreakerState.Open or CircuitBreakerState.HalfOpen;
    }

    /// <inheritdoc />
    public bool IsOpen(MessageLane lane, string consumerIdentity)
    {
        return IsOpen(CircuitBreakerKeys.For(lane, consumerIdentity));
    }

    /// <inheritdoc />
    public async ValueTask RemoveConsumerAsync(string consumerKey)
    {
        if (!_circuits.TryRemove(consumerKey, out var state))
        {
            return;
        }

        var stateLock = state.SyncLock;
        ITimer? timerToDispose;
        Task[] resumeTasks;

        lock (stateLock)
        {
            state.OnPause = null;
            state.OnResume = null;
            _CompleteRetryProbeOutcome(state, CircuitRetryProbeOutcomeKind.Uncertain, nextProbeAt: null);
            state.CurrentEpoch = _NextEpoch();
            timerToDispose = state.OpenTimer;
            state.OpenTimer = null;
            resumeTasks = [.. state.InFlightResumes.Keys];
        }

        // Await timer disposal outside the lock so in-flight callbacks can complete.
        if (timerToDispose is not null)
        {
            await timerToDispose.DisposeAsync().ConfigureAwait(false);
        }

        foreach (var resumeTask in resumeTasks)
        {
            try
            {
                await resumeTask.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                // The resume owner has already logged callback failures. Removal only needs the
                // lifecycle guarantee that the callback is no longer running.
            }
            catch (Exception ex)
            {
                logger.IgnoringResumeTaskFailureDuringRemoval(ex);
            }
        }
    }

    /// <inheritdoc />
    public async ValueTask AbortHalfOpenProbeAsync(string consumerKey)
    {
        if (!_circuits.TryGetValue(consumerKey, out var state))
        {
            return;
        }

        var safeConsumerKey = LogSanitizer.Sanitize(consumerKey);
        var stateLock = state.SyncLock;
        ITimer? oldTimer;
        (TimeSpan OpenDuration, long Epoch) timerInfo;

        lock (stateLock)
        {
            if (state.State is not CircuitBreakerState.HalfOpen)
            {
                return;
            }

            state.ProbeAcquired = false;
            state.ProbeAcquiredEpoch = null;

            // Transition back to Open preserving history.
            // Do NOT increment EscalationLevel — the probe was aborted by teardown,
            // not by a genuine failure. Use inline code instead of _TransitionToOpen
            // to avoid the unintended escalation bump.
            state.State = CircuitBreakerState.Open;
            state.OpenedAt = timeProvider.GetTimestamp();
            state.OpenedAtUtc = timeProvider.GetUtcNow();
            state.CurrentEpoch = _NextEpoch();
            var openDuration = _GetOpenDuration(state);
            _CompleteRetryProbeOutcome(state, CircuitRetryProbeOutcomeKind.Reopened, _GetNextProbeAt(state));

            // HalfOpen state normally has no OpenTimer (the timer already fired to get here),
            // but capture and clear it defensively.
            oldTimer = state.OpenTimer;
            state.OpenTimer = null;

            timerInfo = (openDuration, state.CurrentEpoch);
        }

        logger.CircuitReopenedAfterProbeAbort(safeConsumerKey);

        // Await timer disposal outside the lock
        if (oldTimer is not null)
        {
            await oldTimer.DisposeAsync().ConfigureAwait(false);
        }

        await _CreateAndAssignOpenTimer(state, timerInfo.OpenDuration, timerInfo.Epoch).ConfigureAwait(false);

        // Record a trip metric — we are re-entering Open (counts for operator visibility)
        metrics.RecordTrip(consumerKey);
    }

    /// <inheritdoc />
    public CircuitBreakerState? GetState(string consumerKey)
    {
        Argument.IsNotNull(consumerKey);
        Argument.IsLessThanOrEqualTo(consumerKey.Length, 256);

        if (!_circuits.TryGetValue(consumerKey, out var state))
        {
            return null;
        }

        // No lock needed — State property uses Volatile.Read for cross-thread visibility
        return state.State;
    }

    /// <inheritdoc />
    public CircuitBreakerState? GetState(MessageLane lane, string consumerIdentity)
    {
        return GetState(CircuitBreakerKeys.For(lane, consumerIdentity));
    }

    /// <inheritdoc />
    public IReadOnlySet<string> KnownConsumers => Volatile.Read(ref _knownConsumers);

    /// <inheritdoc />
    public IReadOnlyDictionary<string, CircuitBreakerState> GetAllStates()
    {
        var knownConsumers = Volatile.Read(ref _knownConsumers);
        var capacity = knownConsumers.Count > 0 ? knownConsumers.Count : _circuits.Count;
        var result = new Dictionary<string, CircuitBreakerState>(capacity, StringComparer.Ordinal);

        if (knownConsumers.Count > 0)
        {
            // Emit all known consumers to guarantee OTel gauge shape even before first message.
            // Consumers already in _circuits get their real state; others default to Closed.
            foreach (var consumer in knownConsumers)
            {
                var state = _circuits.TryGetValue(consumer, out var s) ? s.State : CircuitBreakerState.Closed;
                result[consumer] = state;
            }
        }
        else
        {
            foreach (var kvp in _circuits)
            {
                result[kvp.Key] = kvp.Value.State;
            }
        }

        return result;
    }

    /// <inheritdoc />
    public CircuitBreakerSnapshot? GetSnapshot(string consumerKey)
    {
        Argument.IsNotNull(consumerKey);
        Argument.IsLessThanOrEqualTo(consumerKey.Length, 256);

        if (!_circuits.TryGetValue(consumerKey, out var state))
        {
            return null;
        }

        var stateLock = state.SyncLock;

        lock (stateLock)
        {
            var effectiveOpenDuration = _GetOpenDuration(state);
            TimeSpan? remaining = null;

            if (state.State is CircuitBreakerState.Open && state.OpenedAt.HasValue)
            {
                remaining = _GetRemainingOpenDuration(state);
            }

            return new CircuitBreakerSnapshot
            {
                State = state.State,
                EscalationLevel = state.EscalationLevel,
                OpenedAt = state.OpenedAtUtc,
                EstimatedRemainingOpenDuration = remaining,
                ConsecutiveFailures = state.ConsecutiveFailures,
                FailureThreshold = state.EffectiveFailureThreshold,
                EffectiveOpenDuration = effectiveOpenDuration,
            };
        }
    }

    /// <inheritdoc />
    public CircuitBreakerSnapshot? GetSnapshot(MessageLane lane, string consumerIdentity)
    {
        return GetSnapshot(CircuitBreakerKeys.For(lane, consumerIdentity));
    }

    /// <inheritdoc />
    public async ValueTask<bool> ResetAsync(string consumerKey, CancellationToken cancellationToken = default)
    {
        Argument.IsNotNull(consumerKey);
        Argument.IsLessThanOrEqualTo(consumerKey.Length, 256);

        // Must-complete transition: this resumes ALL consumption for the consumer, and aborting mid-flip
        // could leave the breaker in a torn (half-applied) state. Honor the token only here, before the
        // transition begins — it is never raced against the state mutation or the resume callback.
        cancellationToken.ThrowIfCancellationRequested();

        if (!_circuits.TryGetValue(consumerKey, out var state))
        {
            return false;
        }

        var stateLock = state.SyncLock;
        Func<long, ValueTask>? resumeCallback;
        ITimer? timerToDispose;
        CircuitBreakerState previousState;
        long resumeEpoch;

        lock (stateLock)
        {
            previousState = state.State;

            if (previousState is CircuitBreakerState.Closed)
            {
                return false;
            }

            state.State = CircuitBreakerState.Closed;
            state.CurrentEpoch = _NextEpoch();
            state.ConsecutiveFailures = 0;
            state.EscalationLevel = 0;
            state.SuccessfulCyclesAfterClose = 0;
            state.ProbeAcquired = false;
            state.ProbeAcquiredEpoch = null;
            _CompleteRetryProbeOutcome(state, CircuitRetryProbeOutcomeKind.Closed, nextProbeAt: null);
            state.OpenedAt = null;
            state.OpenedAtUtc = null;
            timerToDispose = state.OpenTimer;
            state.OpenTimer = null;

            resumeCallback = state.OnResume;
            resumeEpoch = state.CurrentEpoch;
        }

        logger.CircuitClosedByManualReset(previousState, LogSanitizer.Sanitize(consumerKey));

        if (timerToDispose is not null)
        {
            await timerToDispose.DisposeAsync().ConfigureAwait(false);
        }

        if (resumeCallback is not null)
        {
            await resumeCallback(resumeEpoch).ConfigureAwait(false);
        }

        return true;
    }

    /// <inheritdoc />
    public ValueTask<bool> ResetAsync(
        MessageLane lane,
        string consumerIdentity,
        CancellationToken cancellationToken = default
    )
    {
        return ResetAsync(CircuitBreakerKeys.For(lane, consumerIdentity), cancellationToken);
    }

    /// <inheritdoc />
    public async ValueTask<bool> ForceOpenAsync(string consumerKey, CancellationToken cancellationToken = default)
    {
        Argument.IsNotNull(consumerKey);
        Argument.IsLessThanOrEqualTo(consumerKey.Length, 256);

        // Must-complete transition: this halts ALL consumption for the consumer, and aborting mid-flip
        // could leave the breaker in a torn (half-applied) state. Honor the token only here, before the
        // transition begins — it is never raced against the state mutation or the pause callback.
        cancellationToken.ThrowIfCancellationRequested();

        if (!_circuits.TryGetValue(consumerKey, out var state))
        {
            return false;
        }

        var stateLock = state.SyncLock;
        Func<long, ValueTask>? pauseCallback;
        ITimer? timerToDispose;
        (TimeSpan OpenDuration, long Epoch) timerInfo;
        CircuitBreakerState previousState;
        int escalationLevel;

        lock (stateLock)
        {
            if (state.State is CircuitBreakerState.Open)
            {
                return false;
            }

            previousState = state.State;

            // Force open without incrementing escalation — this is an operator action,
            // not a natural failure. Preserve existing escalation level.
            state.State = CircuitBreakerState.Open;
            state.OpenedAt = timeProvider.GetTimestamp();
            state.OpenedAtUtc = timeProvider.GetUtcNow();
            state.ConsecutiveFailures = 0;
            state.SuccessfulCyclesAfterClose = 0;
            state.ProbeAcquired = false;
            state.ProbeAcquiredEpoch = null;

            var openDuration = _GetOpenDuration(state);
            _CompleteRetryProbeOutcome(state, CircuitRetryProbeOutcomeKind.Reopened, _GetNextProbeAt(state));

            state.CurrentEpoch = _NextEpoch();
            timerInfo = (openDuration, state.CurrentEpoch);
            timerToDispose = state.OpenTimer;
            state.OpenTimer = null;

            pauseCallback = state.OnPause;
            escalationLevel = state.EscalationLevel;
        }

        logger.CircuitForcedOpen(
            previousState,
            LogSanitizer.Sanitize(consumerKey),
            escalationLevel,
            timerInfo.OpenDuration
        );

        if (timerToDispose is not null)
        {
            await timerToDispose.DisposeAsync().ConfigureAwait(false);
        }

        await _CreateAndAssignOpenTimer(state, timerInfo.OpenDuration, timerInfo.Epoch).ConfigureAwait(false);
        metrics.RecordTrip(consumerKey);

        if (pauseCallback is not null)
        {
            await pauseCallback(timerInfo.Epoch).ConfigureAwait(false);
        }

        return true;
    }

    /// <inheritdoc />
    public ValueTask<bool> ForceOpenAsync(
        MessageLane lane,
        string consumerIdentity,
        CancellationToken cancellationToken = default
    )
    {
        return ForceOpenAsync(CircuitBreakerKeys.For(lane, consumerIdentity), cancellationToken);
    }

    /// <summary>
    /// Asynchronously disposes all per-consumer <see cref="Timer"/> instances and cancels
    /// any in-flight resume callbacks. Preferred over <see cref="Dispose"/> because it
    /// can await timer disposal, ensuring no callbacks fire after this method returns.
    /// </summary>
    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        await _disposalCts.CancelAsync().ConfigureAwait(false);

        foreach (var state in _circuits.Values)
        {
            var stateLock = state.SyncLock;
            ITimer? timerToDispose;
            Task[] resumeTasks;

            lock (stateLock)
            {
                state.OnPause = null;
                state.OnResume = null;
                state.CurrentEpoch = _NextEpoch();
                _CompleteRetryProbeOutcome(state, CircuitRetryProbeOutcomeKind.Uncertain, nextProbeAt: null);
                timerToDispose = state.OpenTimer;
                state.OpenTimer = null;
                resumeTasks = [.. state.InFlightResumes.Keys];
            }

            if (timerToDispose is not null)
            {
                await timerToDispose.DisposeAsync().ConfigureAwait(false);
            }

            // Await every pending resume. The TCS makes queued-but-not-started work observable
            // even when cancellation causes Task.Run to skip its body.
            foreach (var resumeTask in resumeTasks)
            {
                try
                {
                    await resumeTask.ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    // Expected — disposal canceled the token
                }
            }
        }

        _disposalCts.Dispose();
    }

    /// <summary>
    /// Synchronously disposes all per-consumer <see cref="Timer"/> instances and blocks on any
    /// in-flight resume tasks to ensure <see cref="_disposalCts"/>
    /// is not disposed while a background task still holds a reference to its token.
    /// Prefer <see cref="DisposeAsync"/> when an async context is available.
    /// </summary>
    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        _disposalCts.Cancel();

        foreach (var state in _circuits.Values)
        {
            var stateLock = state.SyncLock;
            ITimer? timerToDispose;
            Task[] resumeTasks;

            lock (stateLock)
            {
                state.OnPause = null;
                state.OnResume = null;
                state.CurrentEpoch = _NextEpoch();
                _CompleteRetryProbeOutcome(state, CircuitRetryProbeOutcomeKind.Uncertain, nextProbeAt: null);
                timerToDispose = state.OpenTimer;
                state.OpenTimer = null;
                resumeTasks = [.. state.InFlightResumes.Keys];
            }

            timerToDispose?.Dispose();

            // Block on every in-flight resume so _disposalCts isn't disposed while
            // the task still holds a reference to its token, which would cause
            // ObjectDisposedException inside the background task.
            foreach (var resumeTask in resumeTasks)
            {
                try
                {
                    resumeTask.GetAwaiter().GetResult();
                }
                catch (OperationCanceledException)
                { /* expected — disposal canceled the token */
                }
                catch (Exception ex)
                {
                    logger.IgnoringResumeTaskFailureDuringDisposal(ex);
                }
            }
        }

        _disposalCts.Dispose();
    }

    // -------------------------------------------------------------------------
    // Private helpers
    // -------------------------------------------------------------------------

    private long _NextEpoch()
    {
        return Interlocked.Increment(ref _epochCounter);
    }

    /// <summary>
    /// Hard cap on the number of tracked consumers. If exceeded, new consumers receive the no-op state
    /// to prevent unbounded memory growth even if <see cref="_knownConsumers"/> is not yet populated.
    /// <para>
    /// The cap is approximate: under high concurrency, multiple threads may pass the count check
    /// simultaneously and each insert a new key, allowing the dictionary to exceed this value by
    /// the concurrency factor (typically a handful of entries). This is acceptable — the goal is
    /// to prevent unbounded growth, not enforce an exact limit.
    /// </para>
    /// </summary>
    private const int _MaxTrackedConsumers = 1000;

    private TimeSpan _GetRemainingOpenDuration(ConsumerCircuitState state)
    {
        if (!state.OpenedAt.HasValue)
        {
            return TimeSpan.Zero;
        }

        var elapsed = timeProvider.GetElapsedTime(state.OpenedAt.Value);
        var remaining = _GetOpenDuration(state) - elapsed;
        return remaining > TimeSpan.Zero ? remaining : TimeSpan.Zero;
    }

    private DateTimeOffset _GetNextProbeAt(ConsumerCircuitState state)
    {
        // Derive from the injected clock plus the monotonic remaining duration. Reconstructing
        // OpenedAtUtc + open duration would pin the persisted boundary to a stale wall-clock
        // reading if the application clock jumped after the circuit opened (already-due rows
        // get reclaimed and deferred in a loop; a backward jump over-defers the consumer).
        // Every Reopened call site assigns OpenedAt first, so the remaining duration equals the
        // full open duration at reopen time.
        return timeProvider.GetUtcNow().Add(_GetRemainingOpenDuration(state));
    }

    private static void _CompleteRetryProbeOutcome(
        ConsumerCircuitState state,
        CircuitRetryProbeOutcomeKind kind,
        DateTimeOffset? nextProbeAt
    )
    {
        state.RetryProbeOutcome?.TrySetResult(new CircuitRetryProbeOutcome(kind, nextProbeAt));
        state.RetryProbeOutcome = null;
    }

    private ConsumerCircuitState _GetOrAddState(string consumerKey)
    {
        // Fast path: consumer already tracked — no allocation, no contention
        if (_circuits.TryGetValue(consumerKey, out var existingState))
        {
            return existingState;
        }

        var knownConsumers = Volatile.Read(ref _knownConsumers);
        if (knownConsumers.Count > 0 && !knownConsumers.Contains(consumerKey))
        {
            logger.UnrecognizedConsumer(LogSanitizer.Sanitize(consumerKey));

            return _NoOpState;
        }

        // Slow path: consumer not yet tracked
        if (_circuits.Count >= _MaxTrackedConsumers)
        {
            if (Interlocked.CompareExchange(ref _capWarningLogged, 1, 0) == 0)
            {
                logger.CircuitBreakerConsumerCountCapReached(_MaxTrackedConsumers);
            }

            return _NoOpState;
        }

        registry.TryGet(consumerKey, out var perConsumer);

        var newState = new ConsumerCircuitState
        {
            ConsumerKey = consumerKey,
            Enabled = perConsumer?.Enabled ?? true,
            // ReSharper disable once InconsistentlySynchronizedField
            EffectiveFailureThreshold = perConsumer?.FailureThreshold ?? _options.FailureThreshold,
            // ReSharper disable once InconsistentlySynchronizedField
            EffectiveOpenDuration = perConsumer?.OpenDuration ?? _options.OpenDuration,
            // ReSharper disable once InconsistentlySynchronizedField
            EffectiveIsTransient = perConsumer?.IsTransientException ?? _options.IsTransientException,
        };

        return _circuits.GetOrAdd(consumerKey, newState);
    }

    /// <summary>
    /// Must be called while holding the consumer lock. Performs no logging or I/O.
    /// Callers must log the transition and invoke <c>metrics.RecordTrip(consumerKey)</c>
    /// after releasing the lock.
    /// </summary>
    private (
        CircuitBreakerState PreviousState,
        TimeSpan OpenDuration,
        long Epoch,
        int Failures,
        int Escalation,
        ITimer? OldTimerToDispose
    ) _TransitionToOpen(ConsumerCircuitState state)
    {
        var previousState = state.State;
        state.State = CircuitBreakerState.Open;
        state.OpenedAt = timeProvider.GetTimestamp();
        state.OpenedAtUtc = timeProvider.GetUtcNow();
        state.SuccessfulCyclesAfterClose = 0;

        state.EscalationLevel = Math.Min(state.EscalationLevel + 1, 63);
        var openDuration = _GetOpenDuration(state);

        _CompleteRetryProbeOutcome(state, CircuitRetryProbeOutcomeKind.Reopened, _GetNextProbeAt(state));

        state.CurrentEpoch = _NextEpoch();

        // Return the existing timer for disposal outside the lock to avoid potential
        // lock-ordering issues with Timer internals. Safety against stale callbacks comes
        // from the state.State guard in _OnOpenTimerElapsed.
        var oldTimer = state.OpenTimer;
        state.OpenTimer = null;

        return (
            previousState,
            openDuration,
            state.CurrentEpoch,
            state.ConsecutiveFailures,
            state.EscalationLevel,
            oldTimer
        );
    }

    /// <summary>
    /// Creates the open timer outside the consumer lock, then briefly re-acquires the lock
    /// to store it. This avoids holding the lock during Timer construction (heap allocation
    /// and TimerQueue registration which may acquire internal runtime locks).
    /// </summary>
    private async ValueTask _CreateAndAssignOpenTimer(ConsumerCircuitState state, TimeSpan openDuration, long epoch)
    {
        var callbackState = new TimerCallbackState(state, epoch);
        var timer = timeProvider.CreateTimer(
            _OnOpenTimerElapsed,
            callbackState,
            openDuration,
            Timeout.InfiniteTimeSpan
        );
        var stale = false;

        var stateLock = state.SyncLock;

        lock (stateLock)
        {
            // If the epoch has moved on (another thread transitioned the state while we
            // were outside the lock), this timer is already stale — dispose it immediately.
            if (state.CurrentEpoch != epoch || Volatile.Read(ref _disposed) != 0)
            {
                stale = true;
            }
            else
            {
                state.OpenTimer = timer;
            }
        }

        // Disposal happens outside the lock; an early return inside it would leak the stale timer.
        if (stale)
        {
            await timer.DisposeAsync().ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Must be called while holding the consumer lock. Performs no logging or I/O.
    /// Callers must log the transition after releasing the lock.
    /// </summary>
    private (TimeSpan? OpenDuration, ITimer? TimerToDispose) _TransitionToClosed(
        ConsumerCircuitState state,
        bool probeSucceeded
    )
    {
        _CompleteRetryProbeOutcome(state, CircuitRetryProbeOutcomeKind.Closed, nextProbeAt: null);
        state.State = CircuitBreakerState.Closed;
        state.ConsecutiveFailures = 0;
        var timerToDispose = state.OpenTimer;
        state.OpenTimer = null;

        if (probeSucceeded)
        {
            state.SuccessfulCyclesAfterClose++;

            if (state.SuccessfulCyclesAfterClose >= _options.SuccessfulCyclesToResetEscalation)
            {
                state.EscalationLevel = 0;
                state.SuccessfulCyclesAfterClose = 0;
            }
        }
        else
        {
            // Non-transient failure close is not a recovery signal — reset the streak.
            state.SuccessfulCyclesAfterClose = 0;
        }

        TimeSpan? openDuration = null;

        if (state.OpenedAt.HasValue)
        {
            openDuration = timeProvider.GetElapsedTime(state.OpenedAt.Value);
            state.OpenedAt = null;
            state.OpenedAtUtc = null;
        }

        return (openDuration, timerToDispose);
    }

    private void _OnOpenTimerElapsed(object? timerState)
    {
        if (_disposalCts.IsCancellationRequested)
        {
            return;
        }

        var (state, expectedEpoch) = (TimerCallbackState)timerState!;
        var consumerKey = state.ConsumerKey;

        var stateLock = state.SyncLock;
        Func<long, ValueTask>? resumeCallback;
        TaskCompletionSource? resumeTcs = null;
        long resumeEpoch = 0;

        lock (stateLock)
        {
            if (state.State is not CircuitBreakerState.Open || state.CurrentEpoch != expectedEpoch)
            {
                // Circuit was already closed or re-opened — ignore stale timer callback.
                // The epoch check prevents a queued callback from a previous timer
                // (which timer disposal does not cancel) from prematurely transitioning
                // a circuit that has since re-opened with a newer epoch.
                return;
            }

            state.CurrentEpoch = _NextEpoch();
            state.State = CircuitBreakerState.HalfOpen;
            state.RetryProbeOutcome = new TaskCompletionSource<CircuitRetryProbeOutcome>(
                TaskCreationOptions.RunContinuationsAsynchronously
            );
            resumeCallback = state.OnResume;

            // Pre-register the resume BEFORE launching Task.Run so that disposal always
            // sees it. Without this, disposal could acquire the lock
            // between Task.Run launch and the registration, see no work, and return — allowing
            // the resume callback to run after disposal.
            if (resumeCallback is not null)
            {
                var tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                state.InFlightResumes.TryAdd(tcs.Task, 0);
                resumeTcs = tcs;
                resumeEpoch = state.CurrentEpoch;
            }
        }

        logger.CircuitHalfOpen(consumerKey);

        _StartResumeCallback(state, consumerKey, resumeCallback, resumeEpoch, resumeTcs);
    }

    private void _StartResumeCallback(
        ConsumerCircuitState state,
        string consumerKey,
        Func<long, ValueTask>? resumeCallback,
        long epoch,
        TaskCompletionSource? resumeTcs
    )
    {
        if (resumeCallback is not null)
        {
            // Run on a thread-pool thread to avoid blocking the timer callback thread.
            // Use _disposalCts.Token for race-free cancellation — the Volatile.Read(_disposed) check
            // can race with Dispose (callback captures resumeCallback before Dispose nulls it).
            var ct = _disposalCts.Token;

            // Fire-and-forget: the work is tracked through resumeTcs.Task in the consumer's
            // in-flight set. The discard suppresses VSTHRD110/MA0134.
            _ = Task.Run(
                    async () =>
                    {
                        try
                        {
                            if (ct.IsCancellationRequested)
                            {
                                return;
                            }

                            try
                            {
                                await resumeCallback(epoch).ConfigureAwait(false);
                            }
                            catch (Exception ex)
                            {
                                if (ct.IsCancellationRequested)
                                {
                                    return;
                                }

                                logger.ResumeCallbackFailed(ex, LogSanitizer.Sanitize(consumerKey));
                                await _ReopenAfterResumeFailureAsync(consumerKey, epoch).ConfigureAwait(false);
                            }
                        }
                        finally
                        {
                            resumeTcs!.TrySetResult();
                            state.InFlightResumes.TryRemove(resumeTcs.Task, out _);
                        }
                    },
                    ct
                )
                .ContinueWith(
                    // If Task.Run itself is canceled before the body runs (ct already canceled),
                    // the TCS would never complete — complete it here as a fallback.
                    static (_, s) =>
                    {
                        var (source, currentState) = ((TaskCompletionSource, ConsumerCircuitState))s!;
                        source.TrySetResult();
                        currentState.InFlightResumes.TryRemove(source.Task, out byte _);
                    },
                    (resumeTcs, state),
                    CancellationToken.None,
                    TaskContinuationOptions.OnlyOnCanceled,
                    TaskScheduler.Default
                );
        }
    }

    private async Task _ReopenAfterResumeFailureAsync(string consumerKey, long failedEpoch)
    {
        if (_disposalCts.IsCancellationRequested)
        {
            return;
        }

        if (!_circuits.TryGetValue(consumerKey, out var state))
        {
            return;
        }

        var stateLock = state.SyncLock;
        Func<long, ValueTask>? pauseCallback;
        (
            CircuitBreakerState PreviousState,
            TimeSpan OpenDuration,
            long Epoch,
            int Failures,
            int Escalation,
            ITimer? OldTimerToDispose
        ) openInfo;

        lock (stateLock)
        {
            if (state.State is not CircuitBreakerState.HalfOpen || state.CurrentEpoch != failedEpoch)
            {
                return;
            }

            state.ProbeAcquired = false;
            state.ProbeAcquiredEpoch = null;
            openInfo = _TransitionToOpen(state);
            pauseCallback = state.OnPause;
        }

        logger.CircuitOpened(
            openInfo.PreviousState,
            LogSanitizer.Sanitize(consumerKey),
            openInfo.Failures,
            openInfo.Escalation,
            openInfo.OpenDuration
        );

        // Dispose old timer outside the lock
        if (openInfo.OldTimerToDispose is not null)
        {
            await openInfo.OldTimerToDispose.DisposeAsync().ConfigureAwait(false);
        }

        await _CreateAndAssignOpenTimer(state, openInfo.OpenDuration, openInfo.Epoch).ConfigureAwait(false);
        metrics.RecordTrip(consumerKey);

        if (pauseCallback is not null)
        {
            if (_disposalCts.IsCancellationRequested)
            {
                return;
            }

            try
            {
                await pauseCallback(openInfo.Epoch).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                // Circuit is Open but transport may not be paused — inconsistent state.
                // _TransitionToOpen already incremented EscalationLevel, so do NOT bump it
                // again here — that would cause 4x escalation instead of the intended 2x.
                // Log at Critical level so operators are alerted to the inconsistency.
                logger.ReopenPauseCallbackFailed(ex, consumerKey, openInfo.Escalation);
            }
        }
    }

    private TimeSpan _GetOpenDuration(ConsumerCircuitState state)
    {
        var exponent = Math.Max(0, state.EscalationLevel - 1);
        var scaledSeconds = state.EffectiveOpenDuration.TotalSeconds * Math.Pow(2, exponent);

        return TimeSpan.FromSeconds(Math.Min(scaledSeconds, _options.MaxOpenDuration.TotalSeconds));
    }

    // -------------------------------------------------------------------------
    // Inner types
    // -------------------------------------------------------------------------

    /// <summary>
    /// Callback state for <see cref="_OnOpenTimerElapsed"/>. Captures the expected
    /// <see cref="ConsumerCircuitState.CurrentEpoch"/> so stale timer callbacks from
    /// a previous Open cycle are rejected even when the circuit has re-opened.
    /// </summary>
    private sealed record TimerCallbackState(ConsumerCircuitState State, long Epoch);

    private sealed class ConsumerCircuitState
    {
        /// <summary>
        /// Per-consumer lock for all compound check-and-transition operations.
        /// Embedded in the state object to avoid a separate dictionary lookup.
        /// Always assign to a local variable before locking to satisfy the analyzer
        /// (MT1000: locking on publicly accessible member).
        /// </summary>
        public Lock SyncLock { get; } = new();

        private int _state = (int)CircuitBreakerState.Closed;

        /// <summary>
        /// Current circuit state. Uses <see cref="Volatile"/> read/write to ensure
        /// cross-thread visibility on weakly-ordered architectures (e.g. ARM64).
        /// </summary>
        public CircuitBreakerState State
        {
            get => (CircuitBreakerState)Volatile.Read(ref _state);
            set => Volatile.Write(ref _state, (int)value);
        }
#pragma warning disable IDE0032 // False positive: the field is passed by ref to Volatile, which an auto-property cannot back.
        private int _consecutiveFailures;
#pragma warning restore IDE0032

        /// <summary>
        /// Uses <see cref="Volatile"/> read/write for cross-thread visibility on the
        /// fast path in <see cref="ReportSuccessAsync"/> (read outside the lock).
        /// </summary>
        public int ConsecutiveFailures
        {
            get => Volatile.Read(ref _consecutiveFailures);
            set => Volatile.Write(ref _consecutiveFailures, value);
        }

        /// <summary>
        /// Number of times the circuit has opened (including current). Used by <see cref="_GetOpenDuration"/>
        /// to compute escalating durations via <c>baseDuration * 2^(EscalationLevel - 1)</c>.
        /// Incremented BEFORE the duration is computed in <see cref="_TransitionToOpen"/>, so the
        /// first open uses level 1 which maps to exponent 0 (= base duration with no escalation).
        /// </summary>
        public int EscalationLevel { get; set; }

        /// <summary>
        /// Number of successful close cycles since the last escalation reset.
        /// Resets <see cref="EscalationLevel"/> to zero after reaching
        /// <see cref="CircuitBreakerOptions.SuccessfulCyclesToResetEscalation"/>.
        /// </summary>
        public int SuccessfulCyclesAfterClose { get; set; }

        /// <summary>
        /// Monotonic timestamp when the circuit was opened, for duration tracking.
        /// </summary>
        public long? OpenedAt { get; set; }

        /// <summary>
        /// Wall-clock timestamp when the circuit entered the Open state, for snapshot reporting.
        /// <see langword="null"/> when not open.
        /// </summary>
        public DateTimeOffset? OpenedAtUtc { get; set; }

        public Func<long, ValueTask>? OnPause { get; set; }
        public Func<long, ValueTask>? OnResume { get; set; }

        public ITimer? OpenTimer { get; set; }

        /// <summary>
        /// All resume callbacks launched for this generation of consumer state. Disposal and removal
        /// wait on every entry; the per-epoch fence handles the transition order itself.
        /// </summary>
        public ConcurrentDictionary<Task, byte> InFlightResumes { get; } = new();

        /// <summary>
        /// The manager-wide intent epoch currently represented by this circuit state. It is
        /// assigned under <see cref="SyncLock"/> and never repeats across consumer lifetimes.
        /// </summary>
        public long CurrentEpoch { get; set; }

        /// <summary>
        /// Whether a HalfOpen probe has been acquired. Guards single-probe semantics.
        /// Must only be read/written while holding the consumer lock.
        /// </summary>
        public bool ProbeAcquired { get; set; }

        public long? ProbeAcquiredEpoch { get; set; }

        /// <summary>
        /// Shared completion for claims that joined the current HalfOpen probe generation.
        /// Replaced only after the generation reaches a decided outcome or is abandoned.
        /// </summary>
        public TaskCompletionSource<CircuitRetryProbeOutcome>? RetryProbeOutcome { get; set; }

        /// <summary>
        /// The consumer name this state belongs to. Used as timer callback state
        /// to avoid boxing a <c>(string, int)</c> ValueTuple on every circuit trip.
        /// </summary>
        public required string ConsumerKey { get; init; }

        /// <summary>
        /// Whether the circuit breaker is enabled for this consumer. When <see langword="false"/>,
        /// all failure reporting is skipped.
        /// </summary>
        public bool Enabled { get; init; } = true;

        /// <summary>
        /// Resolved failure threshold (per-consumer override or global fallback).
        /// Cached at consumer creation to avoid re-merging on every call.
        /// </summary>
        public required int EffectiveFailureThreshold { get; init; }

        /// <summary>
        /// Resolved open duration (per-consumer override or global fallback).
        /// Cached at consumer creation to avoid re-merging on every call.
        /// </summary>
        public required TimeSpan EffectiveOpenDuration { get; init; }

        /// <summary>
        /// Resolved transient-exception predicate (per-consumer override or global fallback).
        /// Cached at consumer creation to avoid re-merging on every call.
        /// </summary>
        public required Func<Exception, bool> EffectiveIsTransient { get; init; }
    }
}

internal static partial class CircuitBreakerStateManagerLog
{
    [LoggerMessage(
        EventId = 4100,
        Level = LogLevel.Warning,
        Message = "IsTransientException predicate threw for consumer {Consumer}; treating as non-transient"
    )]
    public static partial void IsTransientPredicateFailed(this ILogger logger, Exception exception, string? consumer);

    [LoggerMessage(
        EventId = 4101,
        Level = LogLevel.Warning,
        Message = "Circuit breaker {PreviousState} → Open for consumer {Consumer} (failures: {Failures}, escalation: {Escalation}, open for {Duration})"
    )]
    public static partial void CircuitOpened(
        this ILogger logger,
        CircuitBreakerState previousState,
        string? consumer,
        int failures,
        int escalation,
        TimeSpan duration
    );

    [LoggerMessage(
        EventId = 4102,
        Level = LogLevel.Warning,
        Message = "Circuit breaker HalfOpen → Closed for consumer {Consumer} (non-transient failure, dependency considered healthy)"
    )]
    public static partial void CircuitClosedAfterNonTransientHalfOpenFailure(this ILogger logger, string? consumer);

    public static void CircuitClosedAfterProbeSucceeded(this ILogger logger, string consumerKey)
    {
        if (!logger.IsEnabled(LogLevel.Information))
        {
            return;
        }

        logger.CircuitClosedAfterProbeSucceededCore(LogSanitizer.Sanitize(consumerKey));
    }

    [LoggerMessage(
        EventId = 4103,
        Level = LogLevel.Information,
        Message = "Circuit breaker HalfOpen → Closed for consumer {Consumer} (probe succeeded)"
    )]
    // ReSharper disable once InconsistentNaming
    private static partial void CircuitClosedAfterProbeSucceededCore(this ILogger logger, string? consumer);

    [LoggerMessage(
        EventId = 4104,
        Level = LogLevel.Information,
        Message = "Circuit breaker HalfOpen → Open (probe aborted by transport restart) for consumer {Consumer}"
    )]
    public static partial void CircuitReopenedAfterProbeAbort(this ILogger logger, string? consumer);

    [LoggerMessage(
        EventId = 4105,
        Level = LogLevel.Warning,
        Message = "Circuit breaker {PreviousState} → Closed (manual reset) for consumer {Consumer}"
    )]
    public static partial void CircuitClosedByManualReset(
        this ILogger logger,
        CircuitBreakerState previousState,
        string? consumer
    );

    [LoggerMessage(
        EventId = 4106,
        Level = LogLevel.Warning,
        Message = "Circuit breaker {PreviousState} → Open (forced) for consumer {Consumer} (escalation: {Escalation}, open for {Duration})"
    )]
    public static partial void CircuitForcedOpen(
        this ILogger logger,
        CircuitBreakerState previousState,
        string? consumer,
        int escalation,
        TimeSpan duration
    );

    [LoggerMessage(
        EventId = 4107,
        Level = LogLevel.Debug,
        Message = "Ignoring resume task failure during circuit-breaker disposal."
    )]
    public static partial void IgnoringResumeTaskFailureDuringDisposal(this ILogger logger, Exception exception);

    [LoggerMessage(
        EventId = 4112,
        Level = LogLevel.Debug,
        Message = "Ignoring resume task failure during consumer removal."
    )]
    public static partial void IgnoringResumeTaskFailureDuringRemoval(this ILogger logger, Exception exception);

    [LoggerMessage(
        EventId = 4108,
        Level = LogLevel.Warning,
        Message = "Unrecognized consumer '{Consumer}' — returning no-op circuit state to prevent unbounded cardinality"
    )]
    public static partial void UnrecognizedConsumer(this ILogger logger, string? consumer);

    [LoggerMessage(
        EventId = 4109,
        Level = LogLevel.Warning,
        Message = "Circuit breaker consumer count cap ({Cap}) reached — returning no-op state for new consumers"
    )]
    public static partial void CircuitBreakerConsumerCountCapReached(this ILogger logger, int cap);

    public static void CircuitHalfOpen(this ILogger logger, string consumerKey)
    {
        if (!logger.IsEnabled(LogLevel.Information))
        {
            return;
        }

        logger.CircuitHalfOpenCore(LogSanitizer.Sanitize(consumerKey));
    }

    [LoggerMessage(
        EventId = 4110,
        Level = LogLevel.Information,
        Message = "Circuit breaker Open → HalfOpen for consumer {Consumer}"
    )]
    // ReSharper disable once InconsistentNaming
    private static partial void CircuitHalfOpenCore(this ILogger logger, string? consumer);

    [LoggerMessage(
        EventId = 4111,
        Level = LogLevel.Error,
        Message = "Resume callback failed for consumer {Consumer} during HalfOpen transition"
    )]
    public static partial void ResumeCallbackFailed(this ILogger logger, Exception exception, string? consumer);

    public static void ReopenPauseCallbackFailed(
        this ILogger logger,
        Exception exception,
        string consumerKey,
        int escalation
    )
    {
        if (!logger.IsEnabled(LogLevel.Critical))
        {
            return;
        }

        logger.ReopenPauseCallbackFailedCore(exception, LogSanitizer.Sanitize(consumerKey), escalation);
    }

    [LoggerMessage(
        EventId = 4112,
        Level = LogLevel.Critical,
        Message = "Pause callback failed while re-opening circuit for consumer {Consumer}. Circuit is Open but transport may not be paused — manual ResetAsync may be required (escalation: {Escalation})"
    )]
    // ReSharper disable once InconsistentNaming
    private static partial void ReopenPauseCallbackFailedCore(
        this ILogger logger,
        Exception exception,
        string? consumer,
        int escalation
    );
}
