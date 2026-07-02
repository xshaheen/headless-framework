// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;
using Microsoft.Extensions.Logging;
using Nito.AsyncEx;

#pragma warning disable IDE0130
// ReSharper disable once CheckNamespace
namespace Headless.DistributedLocks;

/// <summary>
/// Background task that periodically validates — and optionally auto-extends — an acquired lease,
/// signalling lease loss by cancelling <see cref="LostToken"/>.
/// </summary>
/// <remarks>
/// <para>
/// The monitoring loop runs at the cadence configured in <see cref="DistributedLockOptions"/> (fraction
/// of the lease TTL). Each iteration calls <see cref="ILeaseHandle.RenewOrValidateLeaseAsync"/> on the
/// owning handle, which delegates to the provider's storage backend.
/// </para>
/// <para>
/// <b>Safety-net self-promotion:</b> if successive iterations all return <see cref="LeaseState.Unknown"/>
/// (transient storage errors) for a cumulative window equal to the full lease TTL, the monitor
/// treats the lease as lost and cancels <see cref="LostToken"/> even without a definitive storage
/// confirmation. This prevents a stuck storage backend from leaving the caller believing it still
/// holds a lease whose TTL has expired.
/// </para>
/// <para>
/// <b>GC-abandonment:</b> the monitoring loop holds only a <see cref="WeakReference{T}"/> to this
/// instance. Callers that drop the handle without disposing it allow the monitor to exit the loop
/// naturally when the GC collects the handle, preventing a memory leak.
/// </para>
/// <para>
/// Dispose is idempotent and thread-safe. A <see cref="LostToken"/> callback that calls
/// <see cref="DisposeAsync"/> is safe — the implementation detects the re-entrant case.
/// </para>
/// </remarks>
internal sealed class LeaseMonitor : IAsyncDisposable
{
    private readonly CancellationTokenSource _disposalSource = new();
    private readonly CancellationTokenSource _handleLostSource = new();
    private readonly ILeaseHandle _leaseHandle;
    private readonly ILogger _logger;
    private readonly TimeSpan _leaseDuration;
    private readonly Lock _syncLock = new();
    private int _disposed;
    private Task? _handleLostCancellationTask;
    private LeaseState _state = LeaseState.Held;
    private LeaseState State
    {
        get
        {
            lock (_syncLock)
            {
                return _state;
            }
        }
    }

    /// <summary>
    /// Creates and immediately starts the background monitoring loop for the given lease handle.
    /// </summary>
    /// <param name="leaseHandle">The handle whose ownership will be monitored.</param>
    /// <param name="timeProvider">Time provider used for cadence scheduling and elapsed-time measurements.</param>
    /// <param name="logger">Logger for state-change and fault events.</param>
    /// <exception cref="ArgumentNullException">Thrown when any parameter is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when <see cref="ILeaseHandle.LeaseDuration"/> is less than
    /// <see cref="ILeaseHandle.MonitoringCadence"/> (the cadence must fit inside the lease window).</exception>
    public LeaseMonitor(ILeaseHandle leaseHandle, TimeProvider timeProvider, ILogger logger)
    {
        _leaseHandle = Argument.IsNotNull(leaseHandle);
        timeProvider = Argument.IsNotNull(timeProvider);
        _logger = Argument.IsNotNull(logger);
        _leaseDuration = Argument.IsGreaterThanOrEqualTo(
            leaseHandle.LeaseDuration,
            leaseHandle.MonitoringCadence,
            paramName: nameof(leaseHandle.LeaseDuration)
        );

        _nudgeSignal = new AsyncAutoResetEvent();

        var loopState = new MonitoringLoopState(
            new WeakReference<LeaseMonitor>(this),
            _nudgeSignal,
            timeProvider,
            leaseHandle.MonitoringCadence,
            leaseHandle.Resource,
            leaseHandle.LeaseId,
            _logger,
            _disposalSource.Token
        );

        // Continuation state holds strong refs to the CTS and logger so the static continuation
        // lambda below does NOT capture `this` — capturing `this` would strong-root the monitor
        // for the lifetime of MonitoringTask and defeat the WeakReference<LeaseMonitor>
        // GC-abandonment invariant (consumers that drop the handle without disposing rely on
        // the loop noticing the dead weak ref and exiting).
        var continuationState = new FaultContinuationState(
            _handleLostSource,
            _logger,
            leaseHandle.Resource,
            leaseHandle.LeaseId
        );

        MonitoringTask = Task.Run(() => _MonitoringLoopAsync(loopState));

        _ = MonitoringTask.ContinueWith(
            static (task, state) =>
            {
                var faultState = (FaultContinuationState)state!;

                // Fail-safe FIRST: a faulted monitor cannot keep providing liveness signals, so
                // signal loss to consumers via LostToken before doing anything that could
                // itself throw (e.g., a misbehaving logger). Catch both ObjectDisposedException
                // (CTS already disposed during teardown) and AggregateException (a registered
                // LostToken callback threw — Cancel() wraps the failures).
                try
                {
#pragma warning disable MA0045 // Cancel() runs in a synchronous ContinueWith delegate and relies on its synchronous AggregateException; it cannot be async.
                    faultState.HandleLostSource.Cancel();
#pragma warning restore MA0045
                }
                catch (ObjectDisposedException)
                {
                    // DisposeAsync already disposed the CTS — fault arrived during/after teardown.
                }
                catch (AggregateException aggregate)
                {
                    // A registered LostToken callback threw. The original fault is still
                    // logged below; log the aggregated callback failures defensively so they are
                    // not silently swallowed.
                    try
                    {
                        faultState.Logger.LogLeaseMonitorFaulted(aggregate, faultState.Resource, faultState.LeaseId);
                    }
#pragma warning disable ERP022, CA1031 // Defensive: a faulting logger must not propagate from this continuation.
                    catch
                    {
                        // Intentionally empty.
                    }
#pragma warning restore ERP022, CA1031
                }

                try
                {
                    faultState.Logger.LogLeaseMonitorFaulted(task.Exception!, faultState.Resource, faultState.LeaseId);
                }
#pragma warning disable ERP022, CA1031 // Defensive: a faulting logger must not propagate from this continuation.
                catch
                {
                    // Intentionally empty.
                }
#pragma warning restore ERP022, CA1031
            },
            continuationState,
            CancellationToken.None,
            TaskContinuationOptions.OnlyOnFaulted,
            TaskScheduler.Default
        );
    }

    // Strong-ref state for the OnlyOnFaulted continuation. Kept separate from MonitoringLoopState
    // (which holds a WeakReference<LeaseMonitor>) so the continuation can fire even after the
    // monitor instance has been GC'd — the CTS and logger are what the fail-safe needs.
    private sealed record FaultContinuationState(
        CancellationTokenSource HandleLostSource,
        ILogger Logger,
        string Resource,
        string LeaseId
    );

    private readonly AsyncAutoResetEvent _nudgeSignal;

    /// <summary>The background task running the monitoring loop. Completes when the loop exits (disposal or lease lost).</summary>
    internal Task MonitoringTask { get; }

    /// <summary>
    /// Cancelled when lease loss is detected (storage confirms ownership is gone, the safety-net
    /// threshold is reached, or the monitoring loop faults). Consumers should register callbacks on
    /// this token to initiate graceful shutdown of work protected by the lease.
    /// </summary>
    public CancellationToken LostToken => _handleLostSource.Token;

    /// <summary>
    /// Signals the monitoring loop to run a validation iteration immediately instead of waiting for
    /// the next scheduled cadence. Used by the release-signal path to surface lease loss with
    /// minimal latency when a <see cref="DistributedLockReleased"/> event is received.
    /// </summary>
    public void TriggerImmediateValidation()
    {
        _nudgeSignal.Set();
    }

    /// <summary>
    /// Stops the monitoring loop and disposes internal resources. Idempotent — safe to call from
    /// a <see cref="LostToken"/> callback or concurrently with the loop. Does not cancel
    /// <see cref="LostToken"/>; that token reflects the lease state, not the monitor's lifetime.
    /// </summary>
    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        var handleLostCancellationTask = Volatile.Read(ref _handleLostCancellationTask);
        var disposeHandleLostSource =
            handleLostCancellationTask is null || Task.CurrentId != handleLostCancellationTask.Id;

        try
        {
            await _disposalSource.CancelAsync().ConfigureAwait(false);
            _nudgeSignal.Set();
#pragma warning disable VSTHRD003 // DisposeAsync is the owner that drains the background monitor task.
            try
            {
                await MonitoringTask.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                // Expected — disposal cancels the loop's wait/storage call.
            }
            catch (Exception exception)
            {
                // A fault in the monitoring loop is already surfaced via the OnlyOnFaulted
                // continuation (logged + LostToken cancelled). Swallow during dispose so
                // teardown cannot throw on the caller of DisposeAsync. Log defensively — a
                // misbehaving logger must not crash teardown.
                try
                {
                    _logger.LogLeaseMonitorFaulted(exception, _leaseHandle.Resource, _leaseHandle.LeaseId);
                }
#pragma warning disable ERP022, CA1031 // Defensive: best-effort log; ignore further logger faults during teardown.
                catch
                {
                    // Intentionally empty.
                }
#pragma warning restore ERP022, CA1031
            }

#pragma warning restore VSTHRD003

            handleLostCancellationTask = Volatile.Read(ref _handleLostCancellationTask);

            if (handleLostCancellationTask is not null && Task.CurrentId != handleLostCancellationTask.Id)
            {
                await handleLostCancellationTask.ConfigureAwait(false);
            }
        }
        finally
        {
            _disposalSource.Dispose();

            if (disposeHandleLostSource)
            {
                _handleLostSource.Dispose();
            }
        }
    }

    private async Task<LeaseState> _RunIterationAsync(TimeSpan leaseLifetime)
    {
        if (_disposalSource.IsCancellationRequested)
        {
            return State;
        }

        // Safety-net self-promotion: only when prior probes were transient (Unknown).
        // A confirmed Held/Renewed from storage MUST NOT trip the safety net — both reset
        // leaseTimestamp in the monitoring loop, so a long Held streak in polling mode never
        // crosses the safety-net threshold. Only Unknown (transient) leaves leaseTimestamp
        // unchanged, allowing the elapsed window to accumulate.
        if (leaseLifetime >= _leaseDuration && State == LeaseState.Unknown)
        {
            _SetState(LeaseState.Lost);
            return LeaseState.Lost;
        }

        LeaseState nextState;

        try
        {
            nextState = await _leaseHandle.RenewOrValidateLeaseAsync(_disposalSource.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (_disposalSource.IsCancellationRequested)
        {
            return State;
        }
        catch (Exception exception)
        {
            _logger.LogLeaseMonitorValidationUnknown(exception, _leaseHandle.Resource, _leaseHandle.LeaseId);
            nextState = LeaseState.Unknown;
        }

        if (_disposalSource.IsCancellationRequested)
        {
            return State;
        }

        _SetState(nextState);

        return nextState;
    }

    private void _SetState(LeaseState nextState)
    {
        bool shouldCancel;
        LeaseState previousState;

        lock (_syncLock)
        {
            if (_state == LeaseState.Lost)
            {
                return;
            }

            if (_state == nextState)
            {
                return;
            }

            previousState = _state;
            _state = nextState;
            shouldCancel = nextState == LeaseState.Lost;
        }

        _logger.LogLeaseMonitorStateChanged(_leaseHandle.Resource, _leaseHandle.LeaseId, previousState, nextState);

        if (!shouldCancel)
        {
            return;
        }

        var cancellationTask = new Task(_CancelHandleLostSource);
        Volatile.Write(ref _handleLostCancellationTask, cancellationTask);
        cancellationTask.Start(TaskScheduler.Default);
    }

    private void _CancelHandleLostSource()
    {
        try
        {
#pragma warning disable MA0045 // Do not use blocking calls, even when the calling method must become async
            _handleLostSource.Cancel();
#pragma warning restore MA0045 // Do not use blocking calls, even when the calling method must become async
        }
        catch (ObjectDisposedException)
        {
            // Disposed concurrently; treat as already-cancelled.
        }
        catch (AggregateException aggregate)
        {
            try
            {
                _logger.LogLeaseMonitorFaulted(aggregate, _leaseHandle.Resource, _leaseHandle.LeaseId);
            }
#pragma warning disable ERP022, CA1031 // Defensive: best-effort log from detached cancellation task.
            catch
            {
                // Intentionally empty.
            }
#pragma warning restore ERP022, CA1031
        }
        finally
        {
            if (Volatile.Read(ref _disposed) != 0)
            {
                _handleLostSource.Dispose();
            }
        }
    }

    private static async Task _MonitoringLoopAsync(MonitoringLoopState state)
    {
        var leaseTimestamp = state.TimeProvider.GetTimestamp();

        while (!state.DisposalToken.IsCancellationRequested)
        {
            using var cadenceSource = state.TimeProvider.CreateCancellationTokenSource(state.Cadence);
            await state.NudgeSignal.SafeWaitAsync(cadenceSource.Token).ConfigureAwait(false);

            if (state.DisposalToken.IsCancellationRequested)
            {
                return;
            }

            if (!state.Monitor.TryGetTarget(out var monitor))
            {
                return;
            }

            var leaseLifetime = state.TimeProvider.GetElapsedTime(leaseTimestamp);
            var nextState = await monitor._RunIterationAsync(leaseLifetime).ConfigureAwait(false);

            // Both Renewed (auto-extend success) and Held (polling-mode positive ownership confirmation)
            // are positive ownership signals from storage and reset the safety-net window. Unknown
            // (transient) does NOT reset — that's the signal the safety net is designed to catch.
            if (nextState is LeaseState.Renewed or LeaseState.Held)
            {
                leaseTimestamp = state.TimeProvider.GetTimestamp();
            }

            if (nextState == LeaseState.Lost)
            {
                return;
            }
        }
    }

    /// <summary>The outcome of a single monitoring iteration, used to drive state transitions.</summary>
    internal enum LeaseState
    {
        /// <summary>Polling mode: storage confirmed the caller still owns the lease/slot.</summary>
        Held,

        /// <summary>Auto-extend mode: the TTL extension call succeeded — ownership is confirmed and the window is reset.</summary>
        Renewed,

        /// <summary>Storage confirmed ownership is gone (another holder or the record is absent).</summary>
        Lost,

        /// <summary>
        /// Transient outcome: storage was unreachable or returned an ambiguous result.
        /// The monitoring loop accumulates these; when the total unknown window reaches the full
        /// lease TTL, the safety net self-promotes to <see cref="Lost"/>.
        /// </summary>
        Unknown,
    }

    /// <summary>
    /// Contract that a lock/slot handle exposes to its attached <see cref="LeaseMonitor"/>. Keeps
    /// the monitor decoupled from concrete handle types (mutex vs. semaphore) while giving it access
    /// to the resource identity, cadence parameters, and the actual storage operation.
    /// </summary>
    internal interface ILeaseHandle
    {
        /// <summary>The resource name for which the lease was acquired.</summary>
        string Resource { get; }

        /// <summary>The unique lease identifier held by this handle.</summary>
        string LeaseId { get; }

        /// <summary>The TTL of the lease as negotiated with the backend at acquire time.</summary>
        TimeSpan LeaseDuration { get; }

        /// <summary>The interval between monitoring iterations (pre-computed from options at handle construction).</summary>
        TimeSpan MonitoringCadence { get; }

        /// <summary>
        /// Performs one monitoring iteration: attempts a renewal (auto-extend mode) or an ownership
        /// validation (polling mode) against the storage backend, and returns the resulting
        /// <see cref="LeaseState"/>.
        /// </summary>
        /// <param name="cancellationToken">Token cancelled when the monitor is being disposed.</param>
        /// <returns>The classification of the iteration outcome.</returns>
        /// <exception cref="OperationCanceledException">Thrown when <paramref name="cancellationToken"/> is cancelled.</exception>
        Task<LeaseState> RenewOrValidateLeaseAsync(CancellationToken cancellationToken);
    }

    private sealed record MonitoringLoopState(
        WeakReference<LeaseMonitor> Monitor,
        AsyncAutoResetEvent NudgeSignal,
        TimeProvider TimeProvider,
        TimeSpan Cadence,
        string Resource,
        string LeaseId,
        ILogger Logger,
        CancellationToken DisposalToken
    );
}

internal static partial class LeaseMonitorLog
{
    /// <summary>Logs a lease monitor state transition (e.g. Held → Lost).</summary>
    [LoggerMessage(
        EventId = 30,
        EventName = "LeaseMonitorStateChanged",
        Level = LogLevel.Debug,
        Message = "Lease monitor state changed: R={Resource} Id={LeaseId} {PreviousState} -> {NextState}"
    )]
    public static partial void LogLeaseMonitorStateChanged(
        this ILogger logger,
        string resource,
        string leaseId,
        LeaseMonitor.LeaseState previousState,
        LeaseMonitor.LeaseState nextState
    );

    /// <summary>Logs a transient validation failure (Unknown state) during a monitoring iteration.</summary>
    [LoggerMessage(
        EventId = 31,
        EventName = "LeaseMonitorValidationUnknown",
        Level = LogLevel.Debug,
        Message = "Lease monitor validation failed transiently: R={Resource} Id={LeaseId}"
    )]
    public static partial void LogLeaseMonitorValidationUnknown(
        this ILogger logger,
        Exception exception,
        string resource,
        string leaseId
    );

    /// <summary>Logs an unhandled exception in the monitoring loop (LostToken is also cancelled).</summary>
    [LoggerMessage(
        EventId = 32,
        EventName = "LeaseMonitorFaulted",
        Level = LogLevel.Error,
        Message = "Lease monitor loop faulted: R={Resource} Id={LeaseId}"
    )]
    public static partial void LogLeaseMonitorFaulted(
        this ILogger logger,
        Exception exception,
        string resource,
        string leaseId
    );
}
