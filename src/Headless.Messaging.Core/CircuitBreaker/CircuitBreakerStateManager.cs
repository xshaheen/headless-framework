// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Headless.Messaging.CircuitBreaker;

/// <summary>
/// Default implementation of <see cref="ICircuitBreakerStateManager"/>.
/// Maintains per-group circuit state and drives Open → HalfOpen transitions via <see cref="Timer"/>.
/// Thread safety is achieved with a per-group <see cref="Lock"/> object (embedded in
/// <see cref="GroupCircuitState"/>) for all compound check-and-transition operations.
/// </summary>
internal sealed class CircuitBreakerStateManager(
    IOptions<CircuitBreakerOptions> options,
    ConsumerCircuitBreakerRegistry registry,
    ILogger<CircuitBreakerStateManager> logger,
    CircuitBreakerMetrics metrics
) : ICircuitBreakerStateManager, IDisposable
{
    private readonly CircuitBreakerOptions _options = options.Value;

    private readonly ConcurrentDictionary<string, GroupCircuitState> _groups =
        new(StringComparer.Ordinal);

    private int _disposed;

    /// <summary>
    /// Known consumer group names registered at startup. When populated, <see cref="_GetOrAddState"/>
    /// returns a static no-op state for unrecognized names to prevent unbounded OTel cardinality.
    /// </summary>
    private IReadOnlySet<string>? _knownGroups;

    /// <summary>
    /// Static no-op state returned for unrecognized group names. Permanently Closed,
    /// disabled, with no real tracking.
    /// </summary>
    private static readonly GroupCircuitState s_noOpState = new()
    {
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
        var frozen = new HashSet<string>(groups, StringComparer.Ordinal);
        _knownGroups = frozen;
        metrics.SetKnownGroups(frozen);
        metrics.RegisterStateCallback(GetAllStates);
    }

    /// <inheritdoc />
    public async ValueTask ReportFailureAsync(string groupName, Exception exception)
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
            logger.LogWarning(ex, "IsTransientException predicate threw for group {Group}; treating as non-transient", groupName);
            isTransient = false;
        }

        var groupLock = state.SyncLock;
        Func<ValueTask>? pauseCallback = null;
        var tripped = false;
        TimeSpan? openDuration = null;

        lock (groupLock)
        {
            switch (state.State)
            {
                case CircuitBreakerState.HalfOpen when !isTransient:
                    // Non-transient failure during probe: the message is bad but the dependency is healthy.
                    // Close the circuit so normal processing resumes.
                    state.ProbeAcquired = false;
                    openDuration = _TransitionToClosed(state, groupName);
                    break;

                case CircuitBreakerState.HalfOpen when isTransient:
                    // Transient failure during probe: dependency still unhealthy — re-open.
                    state.ProbeAcquired = false;
                    _TransitionToOpen(state, groupName);
                    pauseCallback = state.OnPause;
                    tripped = true;
                    break;

                default:
                    if (!isTransient)
                    {
                        // Non-transient failure in Closed/Open state: ignore — not a signal for the breaker.
                        break;
                    }

                    state.ConsecutiveFailures++;

                    if (state.State is CircuitBreakerState.Closed
                        && state.ConsecutiveFailures >= state.EffectiveFailureThreshold)
                    {
                        _TransitionToOpen(state, groupName);
                        pauseCallback = state.OnPause;
                        tripped = true;
                    }

                    break;
            }
        }

        // Emit metrics outside the lock to avoid holding the lock during potentially slow I/O
        if (tripped)
        {
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
    public void ReportSuccess(string groupName)
    {
        if (!_groups.TryGetValue(groupName, out var state))
        {
            return;
        }

        var groupLock = state.SyncLock;
        TimeSpan? openDuration = null;

        lock (groupLock)
        {
            state.ConsecutiveFailures = 0;

            if (state.State is CircuitBreakerState.HalfOpen)
            {
                state.ProbeAcquired = false;
                openDuration = _TransitionToClosed(state, groupName);
            }
        }

        if (openDuration is not null)
        {
            metrics.RecordOpenDuration(groupName, openDuration.Value);
        }
    }

    /// <inheritdoc />
    public bool IsOpen(string groupName)
    {
        if (!_groups.TryGetValue(groupName, out var state))
        {
            return false;
        }

        // No lock needed — State property uses Volatile.Read for cross-thread visibility
        return state.State is CircuitBreakerState.Open or CircuitBreakerState.HalfOpen;
    }

    /// <inheritdoc />
    public void RemoveGroup(string groupName)
    {
        if (!_groups.TryRemove(groupName, out var state))
        {
            return;
        }

        var groupLock = state.SyncLock;

        lock (groupLock)
        {
            state.OnPause = null;
            state.OnResume = null;
            state.OpenTimer?.Dispose();
            state.OpenTimer = null;
        }
    }

    /// <inheritdoc />
    public CircuitBreakerState GetState(string groupName)
    {
        if (!_groups.TryGetValue(groupName, out var state))
        {
            return CircuitBreakerState.Closed;
        }

        // No lock needed — State property uses Volatile.Read for cross-thread visibility
        return state.State;
    }

    /// <inheritdoc />
    public IReadOnlyDictionary<string, CircuitBreakerState> GetAllStates()
    {
        var snapshot = new Dictionary<string, CircuitBreakerState>(_groups.Count, StringComparer.Ordinal);

        foreach (var (group, state) in _groups)
        {
            snapshot[group] = state.State;
        }

        return snapshot;
    }

    /// <inheritdoc />
    public async ValueTask ResetAsync(string groupName, CancellationToken cancellationToken = default)
    {
        if (!_groups.TryGetValue(groupName, out var state))
        {
            return;
        }

        var groupLock = state.SyncLock;
        Func<ValueTask>? resumeCallback = null;
        Timer? timerToDispose = null;

        lock (groupLock)
        {
            var previousState = state.State;

            if (previousState is CircuitBreakerState.Closed)
            {
                return;
            }

            state.State = CircuitBreakerState.Closed;
            state.ConsecutiveFailures = 0;
            state.EscalationLevel = 0;
            state.SuccessfulCyclesAfterClose = 0;
            state.ProbeAcquired = false;
            state.OpenedAt = 0;
            timerToDispose = state.OpenTimer;
            state.OpenTimer = null;

            resumeCallback = state.OnResume;

            logger.LogWarning(
                "Circuit breaker {PreviousState} → Closed (manual reset) for group {Group}",
                previousState,
                groupName
            );
        }

        if (timerToDispose is not null)
        {
            await timerToDispose.DisposeAsync().ConfigureAwait(false);
        }

        if (resumeCallback is not null)
        {
            await resumeCallback().ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Disposes all per-group <see cref="Timer"/> instances held by tracked circuit states.
    /// </summary>
    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        foreach (var state in _groups.Values)
        {
            var groupLock = state.SyncLock;

            lock (groupLock)
            {
                state.OnPause = null;
                state.OnResume = null;
                state.OpenTimer?.Dispose();
                state.OpenTimer = null;
            }
        }
    }

    // -------------------------------------------------------------------------
    // Private helpers
    // -------------------------------------------------------------------------

    /// <summary>
    /// Hard cap on the number of tracked groups. If exceeded, new groups receive the no-op state
    /// to prevent unbounded memory growth even if <see cref="_knownGroups"/> is not yet populated.
    /// </summary>
    private const int MaxTrackedGroups = 1000;

    private GroupCircuitState _GetOrAddState(string groupName)
    {
        if (_knownGroups is not null && !_knownGroups.Contains(groupName))
        {
            logger.LogWarning(
                "Unrecognized consumer group '{Group}' — returning no-op circuit state to prevent unbounded cardinality",
                groupName
            );

            return s_noOpState;
        }

        if (_groups.Count >= MaxTrackedGroups && !_groups.ContainsKey(groupName))
        {
            logger.LogWarning(
                "Circuit breaker group count cap ({Cap}) reached — returning no-op state for group '{Group}'",
                MaxTrackedGroups,
                groupName
            );

            return s_noOpState;
        }

        return _groups.GetOrAdd(
            groupName,
            static (key, ctx) =>
            {
                ctx.Registry.TryGet(key, out var perGroup);

                return new GroupCircuitState
                {
                    Enabled = perGroup?.Enabled ?? true,
                    EffectiveFailureThreshold = perGroup?.FailureThreshold ?? ctx.GlobalOptions.FailureThreshold,
                    EffectiveOpenDuration = perGroup?.OpenDuration ?? ctx.GlobalOptions.OpenDuration,
                    EffectiveIsTransient = perGroup?.IsTransientException ?? ctx.GlobalOptions.IsTransientException,
                };
            },
            (Registry: registry, GlobalOptions: _options)
        );
    }

    /// <summary>
    /// Must be called while holding the group lock.
    /// Callers must invoke <c>metrics.RecordTrip(groupName)</c> after releasing the lock.
    /// </summary>
    private void _TransitionToOpen(GroupCircuitState state, string groupName)
    {
        var previousState = state.State;
        state.State = CircuitBreakerState.Open;
        state.OpenedAt = Environment.TickCount64;
        state.SuccessfulCyclesAfterClose = 0;

        state.EscalationLevel++;
        var openDuration = _GetOpenDuration(state);

        // Increment generation before creating the new timer so that any in-flight callback
        // from the previous timer sees a stale generation and exits early.
        state.TimerGeneration++;
        var generation = state.TimerGeneration;

        // Dispose any existing timer to prevent a stale HalfOpen transition from firing.
        // Timer.Dispose() does not guarantee in-flight callbacks won't fire — safety comes
        // from the generation check in _OnOpenTimerElapsed (plus the state.State guard).
        state.OpenTimer?.Dispose();
        state.OpenTimer = new Timer(
            _OnOpenTimerElapsed,
            (groupName, generation),
            openDuration,
            Timeout.InfiniteTimeSpan
        );

        logger.LogWarning(
            "Circuit breaker {PreviousState} → Open for group {Group} (failures: {Failures}, escalation: {Escalation}, open for {Duration})",
            previousState,
            groupName,
            state.ConsecutiveFailures,
            state.EscalationLevel,
            openDuration
        );
    }

    /// <summary>
    /// Must be called while holding the group lock.
    /// Returns the open duration (or <see langword="null"/> if not tracked) so the caller
    /// can invoke <c>metrics.RecordOpenDuration</c> after releasing the lock.
    /// </summary>
    private TimeSpan? _TransitionToClosed(GroupCircuitState state, string groupName)
    {
        state.State = CircuitBreakerState.Closed;
        state.ConsecutiveFailures = 0;
        state.OpenTimer?.Dispose();
        state.OpenTimer = null;
        state.SuccessfulCyclesAfterClose++;

        if (state.SuccessfulCyclesAfterClose >= _options.SuccessfulCyclesToResetEscalation)
        {
            state.EscalationLevel = 0;
            state.SuccessfulCyclesAfterClose = 0;
        }

        TimeSpan? openDuration = null;

        if (state.OpenedAt > 0)
        {
            openDuration = TimeSpan.FromMilliseconds(Environment.TickCount64 - state.OpenedAt);
            state.OpenedAt = 0;
        }

        logger.LogWarning(
            "Circuit breaker HalfOpen → Closed for group {Group}",
            groupName
        );

        return openDuration;
    }

    private void _OnOpenTimerElapsed(object? timerState)
    {
        if (Volatile.Read(ref _disposed) != 0)
        {
            return;
        }

        var (groupName, generation) = ((string, int))timerState!;

        if (!_groups.TryGetValue(groupName, out var state))
        {
            return;
        }

        var groupLock = state.SyncLock;
        Func<ValueTask>? resumeCallback = null;

        lock (groupLock)
        {
            // Stale callback from a previous timer generation — discard to prevent double-resume.
            if (generation != state.TimerGeneration)
            {
                return;
            }

            if (state.State is not CircuitBreakerState.Open)
            {
                // Circuit was already closed or re-opened — ignore stale timer callback.
                // This guard is the safety net for Timer.Dispose() not guaranteeing that
                // in-flight callbacks are cancelled (see _TransitionToOpen timer disposal).
                return;
            }

            state.State = CircuitBreakerState.HalfOpen;
            resumeCallback = state.OnResume;

            logger.LogWarning(
                "Circuit breaker Open → HalfOpen for group {Group}",
                groupName
            );
        }

        if (resumeCallback is not null)
        {
            // Fire-and-forget on a thread-pool thread to avoid blocking the timer callback thread.
            // ContinueWith observes any unhandled exception (e.g. from _ReopenAfterResumeFailureAsync)
            // to prevent UnobservedTaskException and ensure the failure is logged.
            _ = Task.Run(async () =>
            {
                try
                {
                    await resumeCallback().ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "Resume callback failed for group {Group} during HalfOpen transition", groupName);
                    await _ReopenAfterResumeFailureAsync(groupName).ConfigureAwait(false);
                }
            }).ContinueWith(
                t => logger.LogError(t.Exception, "Unhandled exception in HalfOpen transition for group {Group}", groupName),
                TaskContinuationOptions.OnlyOnFaulted
            );
        }
    }

    private async Task _ReopenAfterResumeFailureAsync(string groupName)
    {
        if (!_groups.TryGetValue(groupName, out var state))
        {
            return;
        }

        var groupLock = state.SyncLock;
        Func<ValueTask>? pauseCallback = null;

        lock (groupLock)
        {
            if (state.State is not CircuitBreakerState.HalfOpen)
            {
                return;
            }

            state.ProbeAcquired = false;
            _TransitionToOpen(state, groupName);
            pauseCallback = state.OnPause;
        }

        metrics.RecordTrip(groupName);

        if (pauseCallback is not null)
        {
            try
            {
                await pauseCallback().ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                // Circuit is Open but transport may not be paused — inconsistent state.
                // Escalate the open duration so the next retry waits longer, and log at
                // Critical level so operators are alerted to the inconsistency.
                int escalation;
                lock (groupLock)
                {
                    state.EscalationLevel++;
                    escalation = state.EscalationLevel;
                }

                logger.LogCritical(
                    ex,
                    "Pause callback failed while re-opening circuit for group {Group}. "
                    + "Circuit is Open but transport may not be paused — manual ResetAsync may be required (escalation: {Escalation})",
                    groupName,
                    escalation
                );
            }
        }
    }

    private TimeSpan _GetOpenDuration(GroupCircuitState state)
    {
        var safeLevel = Math.Min(state.EscalationLevel - 1, 62);
        var seconds = state.EffectiveOpenDuration.TotalSeconds * Math.Pow(2, safeLevel);
        return TimeSpan.FromSeconds(Math.Min(seconds, _options.MaxOpenDuration.TotalSeconds));
    }

    // -------------------------------------------------------------------------
    // Inner state type
    // -------------------------------------------------------------------------

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
        public int ConsecutiveFailures { get; set; }

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

        public Func<ValueTask>? OnPause { get; set; }
        public Func<ValueTask>? OnResume { get; set; }

        public Timer? OpenTimer { get; set; }

        /// <summary>
        /// Monotonically increasing counter incremented each time <see cref="_TransitionToOpen"/> creates
        /// a new timer. Timer callbacks capture the current value and compare it on firing — a mismatch
        /// means the callback is stale and must be discarded.
        /// </summary>
        public int TimerGeneration { get; set; }

        /// <summary>
        /// Whether a HalfOpen probe has been acquired. Guards single-probe semantics.
        /// Must only be read/written while holding the group lock.
        /// </summary>
        public bool ProbeAcquired { get; set; }

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
