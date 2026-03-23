// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Collections.Concurrent;
using System.Collections.Immutable;
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
    private readonly CircuitBreakerOptions _options = options.Value;

    private readonly ConcurrentDictionary<string, GroupCircuitState> _groups =
        new(StringComparer.Ordinal);

    private readonly CancellationTokenSource _disposalCts = new();

    private int _disposed;

    /// <summary>
    /// Flag to ensure the cap-reached warning is logged at most once.
    /// </summary>
    private volatile bool _capWarningLogged;

    /// <summary>
    /// Known consumer group names registered at startup. When populated (non-empty),
    /// <see cref="_GetOrAddState"/> returns a static no-op state for unrecognized names
    /// to prevent unbounded OTel cardinality. Empty before <see cref="RegisterKnownGroups"/> is called.
    /// </summary>
    private IReadOnlySet<string> _knownGroups = ImmutableHashSet<string>.Empty;

    /// <summary>
    /// Static no-op state returned for unrecognized group names. Permanently Closed,
    /// disabled, with no real tracking.
    /// </summary>
    private static readonly GroupCircuitState s_noOpState = new()
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
        var frozen = new HashSet<string>(groups, StringComparer.Ordinal);
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
    public async ValueTask ReportFailureAsync(string groupName, Exception exception, CancellationToken cancellationToken = default)
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
            logger.LogWarning(ex, "IsTransientException predicate threw for group {Group}; treating as non-transient", LogSanitizer.Sanitize(groupName));
            isTransient = false;
        }

        var groupLock = state.SyncLock;
        Func<ValueTask>? pauseCallback = null;
        var tripped = false;
        TimeSpan? openDuration = null;
        Timer? closedTimerToDispose = null;
        (TimeSpan OpenDuration, int Generation) timerInfo = default;

        lock (groupLock)
        {
            switch (state.State)
            {
                case CircuitBreakerState.HalfOpen when !isTransient:
                    // Non-transient failure during probe: the message is bad but the dependency is healthy.
                    // Close the circuit so normal processing resumes.
                    state.ProbeAcquired = false;
                    (openDuration, closedTimerToDispose) = _TransitionToClosed(state, groupName, probeSucceeded: false);
                    break;

                case CircuitBreakerState.HalfOpen when isTransient:
                    // Transient failure during probe: dependency still unhealthy — re-open.
                    state.ProbeAcquired = false;
                    timerInfo = _TransitionToOpen(state, groupName);
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
                        timerInfo = _TransitionToOpen(state, groupName);
                        pauseCallback = state.OnPause;
                        tripped = true;
                    }

                    break;
            }
        }

        // Dispose the timer from _TransitionToClosed outside the lock
        if (closedTimerToDispose is not null)
        {
            await closedTimerToDispose.DisposeAsync().ConfigureAwait(false);
        }

        // Create timer outside the lock to avoid heap allocation and TimerQueue registration
        // while holding it. The generation check inside _CreateAndAssignOpenTimer handles the
        // race where another thread transitions the state between the two lock acquisitions.
        if (tripped)
        {
            _CreateAndAssignOpenTimer(state, groupName, timerInfo.OpenDuration, timerInfo.Generation);
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
    public async ValueTask ReportSuccessAsync(string groupName, CancellationToken cancellationToken = default)
    {
        if (!_groups.TryGetValue(groupName, out var state))
        {
            return;
        }

        // Fast path: skip lock when circuit is Closed and no failures to reset.
        // Volatile read of State + non-locked ConsecutiveFailures is acceptable as
        // a best-effort optimization — the worst case is one extra lock acquisition.
        if (state.State is CircuitBreakerState.Closed && state.ConsecutiveFailures == 0)
        {
            return;
        }

        var groupLock = state.SyncLock;
        TimeSpan? openDuration = null;
        Timer? closedTimerToDispose = null;

        lock (groupLock)
        {
            if (state.State is CircuitBreakerState.Closed)
            {
                state.ConsecutiveFailures = 0;
            }
            else if (state.State is CircuitBreakerState.HalfOpen)
            {
                state.ProbeAcquired = false;
                (openDuration, closedTimerToDispose) = _TransitionToClosed(state, groupName, probeSucceeded: true);
            }
            // Open state: do NOT reset ConsecutiveFailures — preserve failure history
            // so the circuit doesn't close prematurely when the timer transitions to HalfOpen.
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
    public IReadOnlyList<KeyValuePair<string, CircuitBreakerState>> GetAllStates()
    {
        var knownGroups = Volatile.Read(ref _knownGroups);
        var capacity = knownGroups.Count > 0 ? knownGroups.Count : _groups.Count;
        var result = new List<KeyValuePair<string, CircuitBreakerState>>(capacity);

        if (knownGroups.Count > 0)
        {
            // Emit all known groups to guarantee OTel gauge shape even before first message.
            // Groups already in _groups get their real state; others default to Closed.
            foreach (var group in knownGroups)
            {
                var state = _groups.TryGetValue(group, out var s)
                    ? s.State
                    : CircuitBreakerState.Closed;
                result.Add(new KeyValuePair<string, CircuitBreakerState>(group, state));
            }
        }
        else
        {
            foreach (var kvp in _groups)
            {
                result.Add(new KeyValuePair<string, CircuitBreakerState>(kvp.Key, kvp.Value.State));
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
            TimeSpan? remaining = null;

            if (state.State is CircuitBreakerState.Open && state.OpenedAt > 0)
            {
                var openDuration = _GetOpenDuration(state);
                var elapsed = TimeSpan.FromMilliseconds(Environment.TickCount64 - state.OpenedAt);
                var diff = openDuration - elapsed;
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
                EffectiveOpenDuration = _GetOpenDuration(state),
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

        var safeGroupName = LogSanitizer.Sanitize(groupName);
        var groupLock = state.SyncLock;
        Func<ValueTask>? resumeCallback = null;
        Timer? timerToDispose = null;

        lock (groupLock)
        {
            var previousState = state.State;

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

            logger.LogWarning(
                "Circuit breaker {PreviousState} → Closed (manual reset) for group {Group}",
                previousState,
                safeGroupName
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

        var safeGroupName = LogSanitizer.Sanitize(groupName);
        var groupLock = state.SyncLock;
        Func<ValueTask>? pauseCallback = null;
        Timer? timerToDispose = null;
        (TimeSpan OpenDuration, int Generation) timerInfo = default;
        var tripped = false;

        lock (groupLock)
        {
            if (state.State is CircuitBreakerState.Open)
            {
                return false;
            }

            var previousState = state.State;

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
            tripped = true;

            logger.LogWarning(
                "Circuit breaker {PreviousState} → Open (forced) for group {Group} (escalation: {Escalation}, open for {Duration})",
                previousState,
                safeGroupName,
                state.EscalationLevel,
                openDuration
            );
        }

        if (timerToDispose is not null)
        {
            await timerToDispose.DisposeAsync().ConfigureAwait(false);
        }

        if (tripped)
        {
            _CreateAndAssignOpenTimer(state, groupName, timerInfo.OpenDuration, timerInfo.Generation);
            metrics.RecordTrip(groupName);
        }

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
            // The task is already cancelled via _disposalCts, so it should complete quickly.
            if (resumeTask is not null)
            {
                try
                {
                    await resumeTask.ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    // Expected — disposal cancelled the token
                }
            }
        }

        _disposalCts.Dispose();
    }

    /// <summary>
    /// Synchronously disposes all per-group <see cref="Timer"/> instances.
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

            lock (groupLock)
            {
                state.OnPause = null;
                state.OnResume = null;
                state.OpenTimer?.Dispose();
                state.OpenTimer = null;
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
    private const int MaxTrackedGroups = 1000;

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
            logger.LogWarning(
                "Unrecognized consumer group '{Group}' — returning no-op circuit state to prevent unbounded cardinality",
                LogSanitizer.Sanitize(groupName)
            );

            return s_noOpState;
        }

        // Slow path: group not yet tracked
        if (_groups.Count >= MaxTrackedGroups)
        {
            if (!_capWarningLogged)
            {
                _capWarningLogged = true;
                logger.LogWarning(
                    "Circuit breaker group count cap ({Cap}) reached — returning no-op state for new groups",
                    MaxTrackedGroups
                );
            }

            return s_noOpState;
        }

        registry.TryGet(groupName, out var perGroup);

        var newState = new GroupCircuitState
        {
            GroupName = groupName,
            Enabled = perGroup?.Enabled ?? true,
            EffectiveFailureThreshold = perGroup?.FailureThreshold ?? _options.FailureThreshold,
            EffectiveOpenDuration = perGroup?.OpenDuration ?? _options.OpenDuration,
            EffectiveIsTransient = perGroup?.IsTransientException ?? _options.IsTransientException,
        };

        return _groups.GetOrAdd(groupName, newState);
    }

    /// <summary>
    /// Must be called while holding the group lock.
    /// Callers must invoke <c>metrics.RecordTrip(groupName)</c> after releasing the lock.
    /// Returns the open duration and timer generation so the caller can create the timer
    /// outside the lock (to avoid heap allocation and TimerQueue registration while holding it).
    /// </summary>
    private (TimeSpan OpenDuration, int Generation) _TransitionToOpen(GroupCircuitState state, string groupName)
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

        // Dispose any existing timer to prevent a stale HalfOpen transition from firing.
        // Timer.Dispose() does not guarantee in-flight callbacks won't fire — safety comes
        // from the state.State guard in _OnOpenTimerElapsed.
        state.OpenTimer?.Dispose();
        state.OpenTimer = null;

        logger.LogWarning(
            "Circuit breaker {PreviousState} → Open for group {Group} (failures: {Failures}, escalation: {Escalation}, open for {Duration})",
            previousState,
            LogSanitizer.Sanitize(groupName),
            state.ConsecutiveFailures,
            state.EscalationLevel,
            openDuration
        );

        return (openDuration, generation);
    }

    /// <summary>
    /// Creates the open timer outside the group lock, then briefly re-acquires the lock
    /// to store it. This avoids holding the lock during Timer construction (heap allocation
    /// and TimerQueue registration which may acquire internal runtime locks).
    /// </summary>
    private void _CreateAndAssignOpenTimer(GroupCircuitState state, string groupName, TimeSpan openDuration, int generation)
    {
        var timer = new Timer(
            _OnOpenTimerElapsed,
            state,
            openDuration,
            Timeout.InfiniteTimeSpan
        );

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
    /// Must be called while holding the group lock.
    /// Returns the open duration (or <see langword="null"/> if not tracked) so the caller
    /// can invoke <c>metrics.RecordOpenDuration</c> after releasing the lock.
    /// </summary>
    private (TimeSpan? OpenDuration, Timer? TimerToDispose) _TransitionToClosed(GroupCircuitState state, string groupName, bool probeSucceeded)
    {
        state.State = CircuitBreakerState.Closed;
        state.ConsecutiveFailures = 0;
        var timerToDispose = state.OpenTimer;
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
            state.OpenedAtUtc = null;
        }

        if (probeSucceeded)
        {
            logger.LogInformation(
                "Circuit breaker HalfOpen → Closed for group {Group} (probe succeeded)",
                LogSanitizer.Sanitize(groupName)
            );
        }
        else
        {
            logger.LogWarning(
                "Circuit breaker HalfOpen → Closed for group {Group} (non-transient failure, dependency considered healthy)",
                LogSanitizer.Sanitize(groupName)
            );
        }

        return (openDuration, timerToDispose);
    }

    private void _OnOpenTimerElapsed(object? timerState)
    {
        if (_disposalCts.IsCancellationRequested)
        {
            return;
        }

        var state = (GroupCircuitState)timerState!;
        var groupName = state.GroupName;

        var groupLock = state.SyncLock;
        Func<ValueTask>? resumeCallback = null;
        TaskCompletionSource? resumeTcs = null;

        lock (groupLock)
        {
            if (state.State is not CircuitBreakerState.Open)
            {
                // Circuit was already closed or re-opened — ignore stale timer callback.
                // This guard is the safety net for Timer.Dispose() not guaranteeing that
                // in-flight callbacks are cancelled (see _TransitionToOpen timer disposal).
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

            logger.LogInformation(
                "Circuit breaker Open → HalfOpen for group {Group}",
                LogSanitizer.Sanitize(groupName)
            );
        }

        if (resumeCallback is not null)
        {
            // Run on a thread-pool thread to avoid blocking the timer callback thread.
            // Use _disposalCts.Token for race-free cancellation — the Volatile.Read(_disposed) check
            // can race with Dispose (callback captures resumeCallback before Dispose nulls it).
            var ct = _disposalCts.Token;

            // Fire-and-forget: the work is tracked via resumeTcs.Task (assigned to state.ResumeTask),
            // not the Task.Run return value. The discard suppresses VSTHRD110/MA0134.
            _ = Task.Run(async () =>
            {
                try
                {
                    if (ct.IsCancellationRequested) return;

                    try
                    {
                        await resumeCallback().ConfigureAwait(false);
                    }
                    catch (Exception ex)
                    {
                        if (ct.IsCancellationRequested) return;

                        logger.LogError(ex, "Resume callback failed for group {Group} during HalfOpen transition", LogSanitizer.Sanitize(groupName));
                        await _ReopenAfterResumeFailureAsync(groupName).ConfigureAwait(false);
                    }
                }
                finally
                {
                    resumeTcs!.TrySetResult();
                }
            }, ct).ContinueWith(
                // If Task.Run itself is cancelled before the body runs (ct already cancelled),
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
        if (_disposalCts.IsCancellationRequested) return;

        if (!_groups.TryGetValue(groupName, out var state))
        {
            return;
        }

        var groupLock = state.SyncLock;
        Func<ValueTask>? pauseCallback = null;
        (TimeSpan OpenDuration, int Generation) timerInfo;

        lock (groupLock)
        {
            if (state.State is not CircuitBreakerState.HalfOpen)
            {
                return;
            }

            state.ProbeAcquired = false;
            timerInfo = _TransitionToOpen(state, groupName);
            pauseCallback = state.OnPause;
        }

        _CreateAndAssignOpenTimer(state, groupName, timerInfo.OpenDuration, timerInfo.Generation);
        metrics.RecordTrip(groupName);

        if (pauseCallback is not null)
        {
            if (_disposalCts.IsCancellationRequested) return;

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
                logger.LogCritical(
                    ex,
                    "Pause callback failed while re-opening circuit for group {Group}. "
                    + "Circuit is Open but transport may not be paused — manual ResetAsync may be required (escalation: {Escalation})",
                    LogSanitizer.Sanitize(groupName),
                    state.EscalationLevel
                );
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
