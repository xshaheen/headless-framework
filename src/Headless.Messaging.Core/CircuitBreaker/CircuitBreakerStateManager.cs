// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Collections.Concurrent;
using Headless.Checks;
using Headless.Messaging.Internal;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Headless.Messaging.CircuitBreaker;

/// <summary>
/// Default implementation of <see cref="ICircuitBreakerStateManager"/>.
/// Maintains per-group circuit state and drives Open → HalfOpen transitions via <see cref="Timer"/>.
/// Thread safety is achieved with a per-group <see cref="Lock"/> object (embedded in
/// <see cref="GroupCircuitState"/>) for all compound check-and-transition operations.
/// </summary>
/// <remarks>
/// This is a custom circuit breaker rather than Polly's <c>CircuitBreakerStrategyOptions</c> because
/// Polly operates at the per-call pipeline level and cannot coordinate transport-level pause/resume
/// across a consumer group. This implementation provides per-group state tracking, escalating open
/// durations, and direct integration with the transport pause/resume lifecycle and OTel metrics.
/// </remarks>
internal sealed class CircuitBreakerStateManager(
    IOptions<CircuitBreakerOptions> options,
    ConsumerCircuitBreakerRegistry registry,
    ILogger<CircuitBreakerStateManager> logger,
    CircuitBreakerMetrics metrics
) : ICircuitBreakerStateManager, IAsyncDisposable, IDisposable
{
    // Lock-free reads on per-group state are intentional throughout this class:
    //   - GroupCircuitState.State and ConsecutiveFailures use Volatile.Read/Write via their
    //     property accessors (see field comments in GroupCircuitState).
    //   - _disposed uses Interlocked.Exchange for writes and Volatile.Read for reads.
    // ReSharper's InconsistentlySynchronizedField analyzer flags these because the same fields
    // are also touched inside per-group locks — but the lock protects compound state transitions,
    // not single-field visibility, and Volatile/Interlocked provide visibility on their own.
    private readonly CircuitBreakerOptions _options = options.Value;

    private readonly ConcurrentDictionary<string, GroupCircuitState> _groups = new(StringComparer.Ordinal);

    private readonly CancellationTokenSource _disposalCts = new();

    private int _disposed;

    /// <summary>
    /// Flag to ensure the cap-reached warning is logged at most once.
    /// 0 = not logged, 1 = logged. Uses <see cref="Interlocked.CompareExchange(ref int, int, int)"/>
    /// for atomic check-and-set.
    /// </summary>
    private int _capWarningLogged;

    /// <summary>
    /// Known consumer group names registered at startup. When populated (non-empty),
    /// <see cref="_GetOrAddState"/> returns a static no-op state for unrecognized names
    /// to prevent unbounded OTel cardinality. Empty before <see cref="RegisterKnownGroups"/> is called.
    /// </summary>
    private FrozenSet<string> _knownGroups = [];

    /// <summary>
    /// Static no-op state returned for unrecognized group names. Permanently Closed,
    /// disabled, with no real tracking.
    /// </summary>
    private static readonly GroupCircuitState _NoOpState = new()
    {
        GroupName = "_noop",
        Enabled = false,
        EffectiveFailureThreshold = int.MaxValue,
        EffectiveOpenDuration = TimeSpan.MaxValue,
        EffectiveIsTransient = static _ => false,
    };

    /// <inheritdoc />
    public void RegisterGroupCallbacks(string groupName, Func<ValueTask> onPause, Func<ValueTask> onResume)
    {
        var state = _GetOrAddState(groupName);
        var groupLock = state.SyncLock;

        lock (groupLock)
        {
            state.OnPause = onPause;
            state.OnResume = onResume;
        }
    }

    /// <summary>
    /// Freezes the set of valid consumer group names. After this call, <see cref="_GetOrAddState"/>
    /// returns a static no-op state for any group name not in the set, and metrics tag unrecognized
    /// names as <c>_unknown</c>. Should be called once during startup after all consumers are registered.
    /// </summary>
    public void RegisterKnownGroups(IEnumerable<string> groups)
    {
        var frozen = groups.ToFrozenSet(StringComparer.Ordinal);
        Volatile.Write(ref _knownGroups, frozen);

        // Pre-populate state for all known groups so GetAllStates() returns them immediately
        foreach (var group in frozen)
        {
            _GetOrAddState(group);
        }

        metrics.SetKnownGroups(frozen);
        metrics.RegisterStateCallback(GetAllStates);
    }

    /// <inheritdoc />
    public async ValueTask ReportFailureAsync(
        string groupName,
        Exception exception,
        CancellationToken cancellationToken = default
    )
    {
        var state = _GetOrAddState(groupName);

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
            logger.IsTransientPredicateFailed(ex, LogSanitizer.Sanitize(groupName));
            isTransient = false;
        }

        var groupLock = state.SyncLock;
        Func<ValueTask>? pauseCallback = null;
        var tripped = false;
        var closedFromHalfOpen = false;
        TimeSpan? openDuration = null;
        Timer? closedTimerToDispose = null;
        Timer? openTimerToDispose = null;
        (
            CircuitBreakerState PreviousState,
            TimeSpan OpenDuration,
            int Generation,
            int Failures,
            int Escalation,
            Timer? OldTimerToDispose
        ) openInfo = default;

        lock (groupLock)
        {
            switch (state.State)
            {
                case CircuitBreakerState.HalfOpen when !isTransient:
                    // Non-transient failure during probe: the message is bad but the dependency is healthy.
                    // Close the circuit so normal processing resumes.
                    state.ProbeAcquired = false;
                    (openDuration, closedTimerToDispose) = _TransitionToClosed(state, probeSucceeded: false);
                    closedFromHalfOpen = true;
                    break;

                case CircuitBreakerState.HalfOpen when isTransient:
                    // Transient failure during probe: dependency still unhealthy — re-open.
                    state.ProbeAcquired = false;
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
                LogSanitizer.Sanitize(groupName),
                openInfo.Failures,
                openInfo.Escalation,
                openInfo.OpenDuration
            );
        }
        else if (closedFromHalfOpen)
        {
            logger.CircuitClosedAfterNonTransientHalfOpenFailure(LogSanitizer.Sanitize(groupName));
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
            _CreateAndAssignOpenTimer(state, openInfo.OpenDuration, openInfo.Generation);
            metrics.RecordTrip(groupName);
        }

        if (openDuration is not null)
        {
            metrics.RecordOpenDuration(groupName, openDuration.Value);
        }

        if (pauseCallback is not null)
        {
            await pauseCallback().ConfigureAwait(false);
        }
    }

    /// <inheritdoc />
    public bool TryAcquireHalfOpenProbe(string groupName)
    {
        if (!_groups.TryGetValue(groupName, out var state))
        {
            return true;
        }

        var groupLock = state.SyncLock;

        lock (groupLock)
        {
            if (state.State is not CircuitBreakerState.HalfOpen)
            {
                return true;
            }

            if (state.ProbeAcquired)
            {
                return false;
            }

            state.ProbeAcquired = true;
            return true;
        }
    }

    /// <inheritdoc />
    public void ReleaseHalfOpenProbe(string groupName)
    {
        if (!_groups.TryGetValue(groupName, out var state))
        {
            return;
        }

        var groupLock = state.SyncLock;

        lock (groupLock)
        {
            state.ProbeAcquired = false;
        }
    }

    /// <inheritdoc />
    public async ValueTask ReportSuccessAsync(string groupName, CancellationToken cancellationToken = default)
    {
        if (!_groups.TryGetValue(groupName, out var state))
        {
            return;
        }

        // Fast path: skip lock when circuit is Closed and no failures to reset.
        // Both State and ConsecutiveFailures use Volatile reads for ARM64 visibility.
        if (state.State is CircuitBreakerState.Closed && state.ConsecutiveFailures == 0)
        {
            return;
        }

        var groupLock = state.SyncLock;
        TimeSpan? openDuration = null;
        Timer? closedTimerToDispose = null;
        var transitionedToClosed = false;

        lock (groupLock)
        {
            if (state.State is CircuitBreakerState.Closed)
            {
                state.ConsecutiveFailures = 0;
            }
            else if (state.State is CircuitBreakerState.HalfOpen)
            {
                state.ProbeAcquired = false;
                (openDuration, closedTimerToDispose) = _TransitionToClosed(state, probeSucceeded: true);
                transitionedToClosed = true;
            }
            // Open state: do NOT reset ConsecutiveFailures — preserve failure history
            // so the circuit doesn't close prematurely when the timer transitions to HalfOpen.
        }

        if (transitionedToClosed)
        {
            logger.CircuitClosedAfterProbeSucceeded(groupName);
        }

        if (closedTimerToDispose is not null)
        {
            await closedTimerToDispose.DisposeAsync().ConfigureAwait(false);
        }

        if (openDuration is not null)
        {
            metrics.RecordOpenDuration(groupName, openDuration.Value);
        }
    }

    /// <inheritdoc />
    public bool IsOpen(string groupName)
    {
        Argument.IsNotNull(groupName);
        Argument.IsLessThanOrEqualTo(groupName.Length, 256);

        if (!_groups.TryGetValue(groupName, out var state))
        {
            return false;
        }

        // No lock needed — State property uses Volatile.Read for cross-thread visibility
        return state.State is CircuitBreakerState.Open or CircuitBreakerState.HalfOpen;
    }

    /// <inheritdoc />
    public async ValueTask RemoveGroupAsync(string groupName)
    {
        if (!_groups.TryRemove(groupName, out var state))
        {
            return;
        }

        var groupLock = state.SyncLock;
        Timer? timerToDispose;

        lock (groupLock)
        {
            state.OnPause = null;
            state.OnResume = null;
            timerToDispose = state.OpenTimer;
            state.OpenTimer = null;
        }

        // Await timer disposal outside the lock so in-flight callbacks can complete
        if (timerToDispose is not null)
        {
            await timerToDispose.DisposeAsync().ConfigureAwait(false);
        }
    }

    /// <inheritdoc />
    public async ValueTask AbortHalfOpenProbeAsync(string groupName)
    {
        if (!_groups.TryGetValue(groupName, out var state))
        {
            return;
        }

        var safeGroupName = LogSanitizer.Sanitize(groupName);
        var groupLock = state.SyncLock;
        Timer? oldTimer;
        (TimeSpan OpenDuration, int Generation) timerInfo;

        lock (groupLock)
        {
            if (state.State is not CircuitBreakerState.HalfOpen)
            {
                return;
            }

            state.ProbeAcquired = false;

            // Transition back to Open preserving history.
            // Do NOT increment EscalationLevel — the probe was aborted by teardown,
            // not by a genuine failure. Use inline code instead of _TransitionToOpen
            // to avoid the unintended escalation bump.
            state.State = CircuitBreakerState.Open;
            state.OpenedAt = Environment.TickCount64;
            state.OpenedAtUtc = DateTimeOffset.UtcNow;
            state.TimerGeneration++;
            var gen = state.TimerGeneration;
            var openDuration = _GetOpenDuration(state);

            // HalfOpen state normally has no OpenTimer (the timer already fired to get here),
            // but capture and clear it defensively.
            oldTimer = state.OpenTimer;
            state.OpenTimer = null;

            timerInfo = (openDuration, gen);
        }

        logger.CircuitReopenedAfterProbeAbort(safeGroupName);

        // Await timer disposal outside the lock
        if (oldTimer is not null)
        {
            await oldTimer.DisposeAsync().ConfigureAwait(false);
        }

        _CreateAndAssignOpenTimer(state, timerInfo.OpenDuration, timerInfo.Generation);

        // Record a trip metric — we are re-entering Open (counts for operator visibility)
        metrics.RecordTrip(groupName);
    }

    /// <inheritdoc />
    public CircuitBreakerState? GetState(string groupName)
    {
        Argument.IsNotNull(groupName);
        Argument.IsLessThanOrEqualTo(groupName.Length, 256);

        if (!_groups.TryGetValue(groupName, out var state))
        {
            return null;
        }

        // No lock needed — State property uses Volatile.Read for cross-thread visibility
        return state.State;
    }

    /// <inheritdoc />
    public IReadOnlySet<string> KnownGroups => Volatile.Read(ref _knownGroups);

    /// <inheritdoc />
    public IReadOnlyDictionary<string, CircuitBreakerState> GetAllStates()
    {
        var knownGroups = Volatile.Read(ref _knownGroups);
        var capacity = knownGroups.Count > 0 ? knownGroups.Count : _groups.Count;
        var result = new Dictionary<string, CircuitBreakerState>(capacity, StringComparer.Ordinal);

        if (knownGroups.Count > 0)
        {
            // Emit all known groups to guarantee OTel gauge shape even before first message.
            // Groups already in _groups get their real state; others default to Closed.
            foreach (var group in knownGroups)
            {
                var state = _groups.TryGetValue(group, out var s) ? s.State : CircuitBreakerState.Closed;
                result[group] = state;
            }
        }
        else
        {
            foreach (var kvp in _groups)
            {
                result[kvp.Key] = kvp.Value.State;
            }
        }

        return result;
    }

    /// <inheritdoc />
    public CircuitBreakerSnapshot? GetSnapshot(string groupName)
    {
        Argument.IsNotNull(groupName);
        Argument.IsLessThanOrEqualTo(groupName.Length, 256);

        if (!_groups.TryGetValue(groupName, out var state))
        {
            return null;
        }

        var groupLock = state.SyncLock;

        lock (groupLock)
        {
            var effectiveOpenDuration = _GetOpenDuration(state);
            TimeSpan? remaining = null;

            if (state.State is CircuitBreakerState.Open && state.OpenedAt > 0)
            {
                var elapsed = TimeSpan.FromMilliseconds(Environment.TickCount64 - state.OpenedAt);
                var diff = effectiveOpenDuration - elapsed;
                remaining = diff > TimeSpan.Zero ? diff : TimeSpan.Zero;
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
    public async ValueTask<bool> ResetAsync(string groupName)
    {
        Argument.IsNotNull(groupName);
        Argument.IsLessThanOrEqualTo(groupName.Length, 256);

        if (!_groups.TryGetValue(groupName, out var state))
        {
            return false;
        }

        var groupLock = state.SyncLock;
        Func<ValueTask>? resumeCallback;
        Timer? timerToDispose;
        CircuitBreakerState previousState;

        lock (groupLock)
        {
            previousState = state.State;

            if (previousState is CircuitBreakerState.Closed)
            {
                return false;
            }

            state.State = CircuitBreakerState.Closed;
            state.ConsecutiveFailures = 0;
            state.EscalationLevel = 0;
            state.SuccessfulCyclesAfterClose = 0;
            state.ProbeAcquired = false;
            state.OpenedAt = 0;
            state.OpenedAtUtc = null;
            timerToDispose = state.OpenTimer;
            state.OpenTimer = null;

            resumeCallback = state.OnResume;
        }

        logger.CircuitClosedByManualReset(previousState, LogSanitizer.Sanitize(groupName));

        if (timerToDispose is not null)
        {
            await timerToDispose.DisposeAsync().ConfigureAwait(false);
        }

        if (resumeCallback is not null)
        {
            await resumeCallback().ConfigureAwait(false);
        }

        return true;
    }

    /// <inheritdoc />
    public async ValueTask<bool> ForceOpenAsync(string groupName)
    {
        Argument.IsNotNull(groupName);
        Argument.IsLessThanOrEqualTo(groupName.Length, 256);

        if (!_groups.TryGetValue(groupName, out var state))
        {
            return false;
        }

        var groupLock = state.SyncLock;
        Func<ValueTask>? pauseCallback;
        Timer? timerToDispose;
        (TimeSpan OpenDuration, int Generation) timerInfo;
        CircuitBreakerState previousState;
        int escalationLevel;

        lock (groupLock)
        {
            if (state.State is CircuitBreakerState.Open)
            {
                return false;
            }

            previousState = state.State;

            // Force open without incrementing escalation — this is an operator action,
            // not a natural failure. Preserve existing escalation level.
            state.State = CircuitBreakerState.Open;
            state.OpenedAt = Environment.TickCount64;
            state.OpenedAtUtc = DateTimeOffset.UtcNow;
            state.ConsecutiveFailures = 0;
            state.SuccessfulCyclesAfterClose = 0;
            state.ProbeAcquired = false;

            var openDuration = _GetOpenDuration(state);

            // Increment generation and dispose existing timer
            state.TimerGeneration++;
            timerInfo = (openDuration, state.TimerGeneration);
            timerToDispose = state.OpenTimer;
            state.OpenTimer = null;

            pauseCallback = state.OnPause;
            escalationLevel = state.EscalationLevel;
        }

        logger.CircuitForcedOpen(
            previousState,
            LogSanitizer.Sanitize(groupName),
            escalationLevel,
            timerInfo.OpenDuration
        );

        if (timerToDispose is not null)
        {
            await timerToDispose.DisposeAsync().ConfigureAwait(false);
        }

        _CreateAndAssignOpenTimer(state, timerInfo.OpenDuration, timerInfo.Generation);
        metrics.RecordTrip(groupName);

        if (pauseCallback is not null)
        {
            await pauseCallback().ConfigureAwait(false);
        }

        return true;
    }

    /// <summary>
    /// Asynchronously disposes all per-group <see cref="Timer"/> instances and cancels
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

        foreach (var state in _groups.Values)
        {
            var groupLock = state.SyncLock;
            Timer? timerToDispose;
            Task? resumeTask;

            lock (groupLock)
            {
                state.OnPause = null;
                state.OnResume = null;
                timerToDispose = state.OpenTimer;
                state.OpenTimer = null;
                resumeTask = state.ResumeTask;
                state.ResumeTask = null;
            }

            if (timerToDispose is not null)
            {
                await timerToDispose.DisposeAsync().ConfigureAwait(false);
            }

            // Await any pending resume task to ensure no callback runs after disposal.
            // The task is already canceled via _disposalCts, so it should complete quickly.
            if (resumeTask is not null)
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
    /// Synchronously disposes all per-group <see cref="Timer"/> instances and blocks on any
    /// in-flight <see cref="GroupCircuitState.ResumeTask"/> to ensure <see cref="_disposalCts"/>
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

        foreach (var state in _groups.Values)
        {
            var groupLock = state.SyncLock;
            Timer? timerToDispose;
            Task? resumeTask;

            lock (groupLock)
            {
                state.OnPause = null;
                state.OnResume = null;
                timerToDispose = state.OpenTimer;
                state.OpenTimer = null;
                resumeTask = state.ResumeTask;
                state.ResumeTask = null;
            }

            timerToDispose?.Dispose();

            // Block on any in-flight ResumeTask so _disposalCts isn't disposed while
            // the task still holds a reference to its token, which would cause
            // ObjectDisposedException inside the background task.
            if (resumeTask is { IsCompleted: false })
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

    /// <summary>
    /// Hard cap on the number of tracked groups. If exceeded, new groups receive the no-op state
    /// to prevent unbounded memory growth even if <see cref="_knownGroups"/> is not yet populated.
    /// <para>
    /// The cap is approximate: under high concurrency, multiple threads may pass the count check
    /// simultaneously and each insert a new key, allowing the dictionary to exceed this value by
    /// the concurrency factor (typically a handful of entries). This is acceptable — the goal is
    /// to prevent unbounded growth, not enforce an exact limit.
    /// </para>
    /// </summary>
    private const int _MaxTrackedGroups = 1000;

    private GroupCircuitState _GetOrAddState(string groupName)
    {
        // Fast path: group already tracked — no allocation, no contention
        if (_groups.TryGetValue(groupName, out var existingState))
        {
            return existingState;
        }

        var knownGroups = Volatile.Read(ref _knownGroups);
        if (knownGroups.Count > 0 && !knownGroups.Contains(groupName))
        {
            logger.UnrecognizedConsumerGroup(LogSanitizer.Sanitize(groupName));

            return _NoOpState;
        }

        // Slow path: group not yet tracked
        if (_groups.Count >= _MaxTrackedGroups)
        {
            if (Interlocked.CompareExchange(ref _capWarningLogged, 1, 0) == 0)
            {
                logger.CircuitBreakerGroupCountCapReached(_MaxTrackedGroups);
            }

            return _NoOpState;
        }

        registry.TryGet(groupName, out var perGroup);

        var newState = new GroupCircuitState
        {
            GroupName = groupName,
            Enabled = perGroup?.Enabled ?? true,
            // ReSharper disable once InconsistentlySynchronizedField
            EffectiveFailureThreshold = perGroup?.FailureThreshold ?? _options.FailureThreshold,
            // ReSharper disable once InconsistentlySynchronizedField
            EffectiveOpenDuration = perGroup?.OpenDuration ?? _options.OpenDuration,
            // ReSharper disable once InconsistentlySynchronizedField
            EffectiveIsTransient = perGroup?.IsTransientException ?? _options.IsTransientException,
        };

        return _groups.GetOrAdd(groupName, newState);
    }

    /// <summary>
    /// Must be called while holding the group lock. Performs no logging or I/O.
    /// Callers must log the transition and invoke <c>metrics.RecordTrip(groupName)</c>
    /// after releasing the lock.
    /// </summary>
    private (
        CircuitBreakerState PreviousState,
        TimeSpan OpenDuration,
        int Generation,
        int Failures,
        int Escalation,
        Timer? OldTimerToDispose
    ) _TransitionToOpen(GroupCircuitState state)
    {
        var previousState = state.State;
        state.State = CircuitBreakerState.Open;
        state.OpenedAt = Environment.TickCount64;
        state.OpenedAtUtc = DateTimeOffset.UtcNow;
        state.SuccessfulCyclesAfterClose = 0;

        state.EscalationLevel = Math.Min(state.EscalationLevel + 1, 63);
        var openDuration = _GetOpenDuration(state);

        // Increment generation before creating the new timer so that any in-flight callback
        // from the previous timer sees a stale generation and exits early.
        state.TimerGeneration++;
        var generation = state.TimerGeneration;

        // Return the existing timer for disposal outside the lock to avoid potential
        // lock-ordering issues with Timer internals. Safety against stale callbacks comes
        // from the state.State guard in _OnOpenTimerElapsed.
        var oldTimer = state.OpenTimer;
        state.OpenTimer = null;

        return (previousState, openDuration, generation, state.ConsecutiveFailures, state.EscalationLevel, oldTimer);
    }

    /// <summary>
    /// Creates the open timer outside the group lock, then briefly re-acquires the lock
    /// to store it. This avoids holding the lock during Timer construction (heap allocation
    /// and TimerQueue registration which may acquire internal runtime locks).
    /// </summary>
    private void _CreateAndAssignOpenTimer(GroupCircuitState state, TimeSpan openDuration, int generation)
    {
        var callbackState = new TimerCallbackState(state, generation);
        var timer = new Timer(_OnOpenTimerElapsed, callbackState, openDuration, Timeout.InfiniteTimeSpan);

        var groupLock = state.SyncLock;

        lock (groupLock)
        {
            // If the generation has moved on (another thread transitioned the state while we
            // were outside the lock), this timer is already stale — dispose it immediately.
            if (state.TimerGeneration != generation || Volatile.Read(ref _disposed) != 0)
            {
                timer.Dispose();
                return;
            }

            state.OpenTimer = timer;
        }
    }

    /// <summary>
    /// Must be called while holding the group lock. Performs no logging or I/O.
    /// Callers must log the transition after releasing the lock.
    /// </summary>
    private (TimeSpan? OpenDuration, Timer? TimerToDispose) _TransitionToClosed(
        GroupCircuitState state,
        bool probeSucceeded
    )
    {
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

        if (state.OpenedAt > 0)
        {
            openDuration = TimeSpan.FromMilliseconds(Environment.TickCount64 - state.OpenedAt);
            state.OpenedAt = 0;
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

        var (state, expectedGeneration) = (TimerCallbackState)timerState!;
        var groupName = state.GroupName;

        var groupLock = state.SyncLock;
        Func<ValueTask>? resumeCallback;
        TaskCompletionSource? resumeTcs = null;

        lock (groupLock)
        {
            if (state.State is not CircuitBreakerState.Open || state.TimerGeneration != expectedGeneration)
            {
                // Circuit was already closed or re-opened — ignore stale timer callback.
                // The generation check prevents a queued callback from a previous timer
                // (which Timer.Dispose does not cancel) from prematurely transitioning
                // a circuit that has since re-opened with a new generation.
                return;
            }

            state.State = CircuitBreakerState.HalfOpen;
            resumeCallback = state.OnResume;

            // Pre-assign ResumeTask BEFORE launching Task.Run so that DisposeAsync always
            // sees the in-flight task. Without this, DisposeAsync could acquire the lock
            // between Task.Run launch and the assignment, see null, and return — allowing
            // the resume callback to run after disposal.
            if (resumeCallback is not null)
            {
                var tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                state.ResumeTask = tcs.Task;
                resumeTcs = tcs;
            }
        }

        logger.CircuitHalfOpen(groupName);

        if (resumeCallback is not null)
        {
            // Run on a thread-pool thread to avoid blocking the timer callback thread.
            // Use _disposalCts.Token for race-free cancellation — the Volatile.Read(_disposed) check
            // can race with Dispose (callback captures resumeCallback before Dispose nulls it).
            var ct = _disposalCts.Token;

            // Fire-and-forget: the work is tracked via resumeTcs.Task (assigned to state.ResumeTask),
            // not the Task.Run return value. The discard suppresses VSTHRD110/MA0134.
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
                                await resumeCallback().ConfigureAwait(false);
                            }
                            catch (Exception ex)
                            {
                                if (ct.IsCancellationRequested)
                                {
                                    return;
                                }

                                logger.ResumeCallbackFailed(ex, LogSanitizer.Sanitize(groupName));
                                await _ReopenAfterResumeFailureAsync(groupName).ConfigureAwait(false);
                            }
                        }
                        finally
                        {
                            resumeTcs!.TrySetResult();
                        }
                    },
                    ct
                )
                .ContinueWith(
                    // If Task.Run itself is canceled before the body runs (ct already canceled),
                    // the TCS would never complete — complete it here as a fallback.
                    static (_, s) => ((TaskCompletionSource)s!).TrySetResult(),
                    resumeTcs,
                    CancellationToken.None,
                    TaskContinuationOptions.OnlyOnCanceled,
                    TaskScheduler.Default
                );
        }
    }

    private async Task _ReopenAfterResumeFailureAsync(string groupName)
    {
        if (_disposalCts.IsCancellationRequested)
        {
            return;
        }

        if (!_groups.TryGetValue(groupName, out var state))
        {
            return;
        }

        var groupLock = state.SyncLock;
        Func<ValueTask>? pauseCallback;
        (
            CircuitBreakerState PreviousState,
            TimeSpan OpenDuration,
            int Generation,
            int Failures,
            int Escalation,
            Timer? OldTimerToDispose
        ) openInfo;

        lock (groupLock)
        {
            if (state.State is not CircuitBreakerState.HalfOpen)
            {
                return;
            }

            state.ProbeAcquired = false;
            openInfo = _TransitionToOpen(state);
            pauseCallback = state.OnPause;
        }

        logger.CircuitOpened(
            openInfo.PreviousState,
            LogSanitizer.Sanitize(groupName),
            openInfo.Failures,
            openInfo.Escalation,
            openInfo.OpenDuration
        );

        // Dispose old timer outside the lock
        if (openInfo.OldTimerToDispose is not null)
        {
            await openInfo.OldTimerToDispose.DisposeAsync().ConfigureAwait(false);
        }

        _CreateAndAssignOpenTimer(state, openInfo.OpenDuration, openInfo.Generation);
        metrics.RecordTrip(groupName);

        if (pauseCallback is not null)
        {
            if (_disposalCts.IsCancellationRequested)
            {
                return;
            }

            try
            {
                await pauseCallback().ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                // Circuit is Open but transport may not be paused — inconsistent state.
                // _TransitionToOpen already incremented EscalationLevel, so do NOT bump it
                // again here — that would cause 4x escalation instead of the intended 2x.
                // Log at Critical level so operators are alerted to the inconsistency.
                logger.ReopenPauseCallbackFailed(ex, groupName, openInfo.Escalation);
            }
        }
    }

    private TimeSpan _GetOpenDuration(GroupCircuitState state)
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
    /// <see cref="GroupCircuitState.TimerGeneration"/> so stale timer callbacks from
    /// a previous Open cycle are rejected even when the circuit has re-opened.
    /// </summary>
    private sealed record TimerCallbackState(GroupCircuitState State, int Generation);

    private sealed class GroupCircuitState
    {
        /// <summary>
        /// Per-group lock for all compound check-and-transition operations.
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
#pragma warning disable IDE0032
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
        /// Tick count when the circuit was opened, for duration tracking. 0 = not open.
        /// </summary>
        public long OpenedAt { get; set; }

        /// <summary>
        /// Wall-clock timestamp when the circuit entered the Open state, for snapshot reporting.
        /// <see langword="null"/> when not open.
        /// </summary>
        public DateTimeOffset? OpenedAtUtc { get; set; }

        public Func<ValueTask>? OnPause { get; set; }
        public Func<ValueTask>? OnResume { get; set; }

        public Timer? OpenTimer { get; set; }

        /// <summary>
        /// Task reference for the in-flight resume callback launched by <see cref="_OnOpenTimerElapsed"/>.
        /// <see cref="DisposeAsync"/> awaits this to prevent the callback from running after disposal.
        /// </summary>
        public Task? ResumeTask { get; set; }

        /// <summary>
        /// Monotonically increasing counter incremented each time <see cref="_TransitionToOpen"/> creates
        /// a new timer. Used by <see cref="_CreateAndAssignOpenTimer"/> to detect whether the state was
        /// re-transitioned while the timer was being constructed outside the lock.
        /// </summary>
        public int TimerGeneration { get; set; }

        /// <summary>
        /// Whether a HalfOpen probe has been acquired. Guards single-probe semantics.
        /// Must only be read/written while holding the group lock.
        /// </summary>
        public bool ProbeAcquired { get; set; }

        /// <summary>
        /// The consumer group name this state belongs to. Used as timer callback state
        /// to avoid boxing a <c>(string, int)</c> ValueTuple on every circuit trip.
        /// </summary>
        public required string GroupName { get; init; }

        /// <summary>
        /// Whether the circuit breaker is enabled for this group. When <see langword="false"/>,
        /// all failure reporting is skipped.
        /// </summary>
        public bool Enabled { get; init; } = true;

        /// <summary>
        /// Resolved failure threshold (per-group override or global fallback).
        /// Cached at group creation to avoid re-merging on every call.
        /// </summary>
        public required int EffectiveFailureThreshold { get; init; }

        /// <summary>
        /// Resolved open duration (per-group override or global fallback).
        /// Cached at group creation to avoid re-merging on every call.
        /// </summary>
        public required TimeSpan EffectiveOpenDuration { get; init; }

        /// <summary>
        /// Resolved transient-exception predicate (per-group override or global fallback).
        /// Cached at group creation to avoid re-merging on every call.
        /// </summary>
        public required Func<Exception, bool> EffectiveIsTransient { get; init; }
    }
}

internal static partial class CircuitBreakerStateManagerLog
{
    [LoggerMessage(
        EventId = 4100,
        Level = LogLevel.Warning,
        Message = "IsTransientException predicate threw for group {Group}; treating as non-transient"
    )]
    public static partial void IsTransientPredicateFailed(this ILogger logger, Exception exception, string? group);

    [LoggerMessage(
        EventId = 4101,
        Level = LogLevel.Warning,
        Message = "Circuit breaker {PreviousState} → Open for group {Group} (failures: {Failures}, escalation: {Escalation}, open for {Duration})"
    )]
    public static partial void CircuitOpened(
        this ILogger logger,
        CircuitBreakerState previousState,
        string? group,
        int failures,
        int escalation,
        TimeSpan duration
    );

    [LoggerMessage(
        EventId = 4102,
        Level = LogLevel.Warning,
        Message = "Circuit breaker HalfOpen → Closed for group {Group} (non-transient failure, dependency considered healthy)"
    )]
    public static partial void CircuitClosedAfterNonTransientHalfOpenFailure(this ILogger logger, string? group);

    public static void CircuitClosedAfterProbeSucceeded(this ILogger logger, string groupName)
    {
        if (!logger.IsEnabled(LogLevel.Information))
        {
            return;
        }

        logger.CircuitClosedAfterProbeSucceededCore(LogSanitizer.Sanitize(groupName));
    }

    [LoggerMessage(
        EventId = 4103,
        Level = LogLevel.Information,
        Message = "Circuit breaker HalfOpen → Closed for group {Group} (probe succeeded)"
    )]
    // ReSharper disable once InconsistentNaming
    private static partial void CircuitClosedAfterProbeSucceededCore(this ILogger logger, string? group);

    [LoggerMessage(
        EventId = 4104,
        Level = LogLevel.Information,
        Message = "Circuit breaker HalfOpen → Open (probe aborted by transport restart) for group {Group}"
    )]
    public static partial void CircuitReopenedAfterProbeAbort(this ILogger logger, string? group);

    [LoggerMessage(
        EventId = 4105,
        Level = LogLevel.Warning,
        Message = "Circuit breaker {PreviousState} → Closed (manual reset) for group {Group}"
    )]
    public static partial void CircuitClosedByManualReset(
        this ILogger logger,
        CircuitBreakerState previousState,
        string? group
    );

    [LoggerMessage(
        EventId = 4106,
        Level = LogLevel.Warning,
        Message = "Circuit breaker {PreviousState} → Open (forced) for group {Group} (escalation: {Escalation}, open for {Duration})"
    )]
    public static partial void CircuitForcedOpen(
        this ILogger logger,
        CircuitBreakerState previousState,
        string? group,
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
        EventId = 4108,
        Level = LogLevel.Warning,
        Message = "Unrecognized consumer group '{Group}' — returning no-op circuit state to prevent unbounded cardinality"
    )]
    public static partial void UnrecognizedConsumerGroup(this ILogger logger, string? group);

    [LoggerMessage(
        EventId = 4109,
        Level = LogLevel.Warning,
        Message = "Circuit breaker group count cap ({Cap}) reached — returning no-op state for new groups"
    )]
    public static partial void CircuitBreakerGroupCountCapReached(this ILogger logger, int cap);

    public static void CircuitHalfOpen(this ILogger logger, string groupName)
    {
        if (!logger.IsEnabled(LogLevel.Information))
        {
            return;
        }

        logger.CircuitHalfOpenCore(LogSanitizer.Sanitize(groupName));
    }

    [LoggerMessage(
        EventId = 4110,
        Level = LogLevel.Information,
        Message = "Circuit breaker Open → HalfOpen for group {Group}"
    )]
    // ReSharper disable once InconsistentNaming
    private static partial void CircuitHalfOpenCore(this ILogger logger, string? group);

    [LoggerMessage(
        EventId = 4111,
        Level = LogLevel.Error,
        Message = "Resume callback failed for group {Group} during HalfOpen transition"
    )]
    public static partial void ResumeCallbackFailed(this ILogger logger, Exception exception, string? group);

    public static void ReopenPauseCallbackFailed(
        this ILogger logger,
        Exception exception,
        string groupName,
        int escalation
    )
    {
        if (!logger.IsEnabled(LogLevel.Critical))
        {
            return;
        }

        logger.ReopenPauseCallbackFailedCore(exception, LogSanitizer.Sanitize(groupName), escalation);
    }

    [LoggerMessage(
        EventId = 4112,
        Level = LogLevel.Critical,
        Message = "Pause callback failed while re-opening circuit for group {Group}. Circuit is Open but transport may not be paused — manual ResetAsync may be required (escalation: {Escalation})"
    )]
    // ReSharper disable once InconsistentNaming
    private static partial void ReopenPauseCallbackFailedCore(
        this ILogger logger,
        Exception exception,
        string? group,
        int escalation
    );
}
