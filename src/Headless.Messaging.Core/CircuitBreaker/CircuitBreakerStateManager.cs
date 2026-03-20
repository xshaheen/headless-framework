// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Headless.Messaging.CircuitBreaker;

/// <summary>
/// Default implementation of <see cref="ICircuitBreakerStateManager"/>.
/// Maintains per-group circuit state and drives Open → HalfOpen transitions via <see cref="Timer"/>.
/// Thread safety is achieved with a per-group <see cref="Lock"/> object for all compound
/// check-and-transition operations.
/// </summary>
internal sealed class CircuitBreakerStateManager(
    IOptions<CircuitBreakerOptions> options,
    ConsumerCircuitBreakerRegistry registry,
    ILogger<CircuitBreakerStateManager> logger,
    CircuitBreakerMetrics metrics
) : ICircuitBreakerStateManager, ICircuitBreakerMonitor
{
    private readonly CircuitBreakerOptions _options = options.Value;

    // Lock objects are stored separately from the state so the analyzer never sees
    // locking on a property accessor. We always lock on the local variable 'groupLock'.
    private readonly ConcurrentDictionary<string, GroupCircuitState> _groups =
        new(StringComparer.Ordinal);

    private readonly ConcurrentDictionary<string, Lock> _locks = new(StringComparer.Ordinal);

    /// <inheritdoc />
    public void RegisterGroupCallbacks(string groupName, Func<ValueTask> onPause, Func<ValueTask> onResume)
    {
        var state = _GetOrAddState(groupName);
        var groupLock = _locks[groupName];

        lock (groupLock)
        {
            state.OnPause = onPause;
            state.OnResume = onResume;
        }
    }

    /// <inheritdoc />
    public async ValueTask ReportFailureAsync(string groupName, Exception exception)
    {
        var state = _GetOrAddState(groupName);

        if (!state.Enabled)
        {
            return;
        }

        var isTransient = state.EffectiveIsTransient(exception);
        var groupLock = _locks[groupName];
        Func<ValueTask>? pauseCallback = null;

        lock (groupLock)
        {
            switch (state.State)
            {
                case CircuitBreakerState.HalfOpen when !isTransient:
                    // Non-transient failure during probe: the message is bad but the dependency is healthy.
                    // Close the circuit so normal processing resumes.
                    _TryReleaseProbeSemaphore(state);
                    _TransitionToClosed(state, groupName);
                    break;

                case CircuitBreakerState.HalfOpen when isTransient:
                    // Transient failure during probe: dependency still unhealthy — re-open.
                    _TryReleaseProbeSemaphore(state);
                    _TransitionToOpen(state, groupName);
                    pauseCallback = state.OnPause;
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
                    }

                    break;
            }
        }

        if (pauseCallback is not null)
        {
            await pauseCallback().ConfigureAwait(false);
        }
    }

    /// <inheritdoc />
    public void ReportSuccess(string groupName)
    {
        if (!_groups.TryGetValue(groupName, out var state))
        {
            return;
        }

        var groupLock = _locks[groupName];

        lock (groupLock)
        {
            state.ConsecutiveFailures = 0;

            if (state.State is CircuitBreakerState.HalfOpen)
            {
                _TryReleaseProbeSemaphore(state);
                _TransitionToClosed(state, groupName);
            }
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
    public bool TryAcquireProbePermit(string groupName)
    {
        if (!_groups.TryGetValue(groupName, out var state))
        {
            return false;
        }

        return state.ProbePermit.Wait(0);
    }

    // -------------------------------------------------------------------------
    // Private helpers
    // -------------------------------------------------------------------------

    private GroupCircuitState _GetOrAddState(string groupName)
    {
        return _groups.GetOrAdd(
            groupName,
            static (key, ctx) =>
            {
                ctx.Locks.TryAdd(key, new Lock());

                ctx.Registry.TryGet(key, out var perGroup);

                return new GroupCircuitState
                {
                    Enabled = perGroup?.Enabled ?? true,
                    EffectiveFailureThreshold = perGroup?.FailureThreshold ?? ctx.GlobalOptions.FailureThreshold,
                    EffectiveOpenDuration = perGroup?.OpenDuration ?? ctx.GlobalOptions.OpenDuration,
                    EffectiveIsTransient = perGroup?.IsTransientException ?? ctx.GlobalOptions.IsTransientException,
                };
            },
            (Locks: _locks, Registry: registry, GlobalOptions: _options)
        );
    }

    /// <summary>Must be called while holding the group lock.</summary>
    private void _TransitionToOpen(GroupCircuitState state, string groupName)
    {
        var previousState = state.State;
        state.State = CircuitBreakerState.Open;
        state.OpenedAt = Environment.TickCount64;

        var openDuration = _GetOpenDuration(state);
        state.EscalationLevel++;

        // Dispose any existing timer to prevent a stale HalfOpen transition from firing
        state.OpenTimer?.Dispose();
        state.OpenTimer = new Timer(
            _OnOpenTimerElapsed,
            groupName,
            openDuration,
            Timeout.InfiniteTimeSpan
        );

        metrics.RecordTrip(groupName);

        logger.LogWarning(
            "Circuit breaker {PreviousState} → Open for group {Group} (failures: {Failures}, escalation: {Escalation}, open for {Duration})",
            previousState,
            groupName,
            state.ConsecutiveFailures,
            state.EscalationLevel,
            openDuration
        );
    }

    /// <summary>Must be called while holding the group lock.</summary>
    private void _TransitionToClosed(GroupCircuitState state, string groupName)
    {
        state.State = CircuitBreakerState.Closed;
        state.OpenTimer?.Dispose();
        state.OpenTimer = null;
        state.SuccessfulCyclesAfterClose++;

        if (state.SuccessfulCyclesAfterClose >= _options.SuccessfulCyclesToResetEscalation)
        {
            state.EscalationLevel = 0;
            state.SuccessfulCyclesAfterClose = 0;
        }

        if (state.OpenedAt > 0)
        {
            var openMs = Environment.TickCount64 - state.OpenedAt;
            metrics.RecordOpenDuration(groupName, openMs);
            state.OpenedAt = 0;
        }

        logger.LogWarning(
            "Circuit breaker HalfOpen → Closed for group {Group}",
            groupName
        );
    }

    /// <summary>
    /// Releases the probe semaphore if it was previously acquired (i.e. if its current count is 0).
    /// Must be called while holding the group lock.
    /// </summary>
    private static void _TryReleaseProbeSemaphore(GroupCircuitState state)
    {
        if (state.ProbePermit.CurrentCount == 0)
        {
            state.ProbePermit.Release();
        }
    }

    private void _OnOpenTimerElapsed(object? timerState)
    {
        var groupName = (string)timerState!;

        if (!_groups.TryGetValue(groupName, out var state) || !_locks.TryGetValue(groupName, out var groupLock))
        {
            return;
        }

        Func<ValueTask>? resumeCallback = null;

        lock (groupLock)
        {
            if (state.State is not CircuitBreakerState.Open)
            {
                // Circuit was already closed or re-opened — ignore stale timer
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
            // Fire-and-forget on a thread-pool thread to avoid blocking the timer callback thread
            _ = Task.Run(async () =>
            {
                try
                {
                    await resumeCallback().ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "Resume callback failed for group {Group} during HalfOpen transition", groupName);
                }
            });
        }
    }

    private TimeSpan _GetOpenDuration(GroupCircuitState state)
    {
        var safeLevel = Math.Min(state.EscalationLevel, 62);
        var seconds = state.EffectiveOpenDuration.TotalSeconds * Math.Pow(2, safeLevel);
        return TimeSpan.FromSeconds(Math.Min(seconds, _options.MaxOpenDuration.TotalSeconds));
    }

    // -------------------------------------------------------------------------
    // Inner state type
    // -------------------------------------------------------------------------

    private sealed class GroupCircuitState
    {
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
        /// Zero-based escalation level. Incremented each time the circuit opens.
        /// Controls the exponential back-off of <see cref="CircuitBreakerOptions.OpenDuration"/>.
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
        /// Semaphore limiting HalfOpen to a single concurrent probe message.
        /// </summary>
        public SemaphoreSlim ProbePermit { get; } = new(1, 1);

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
