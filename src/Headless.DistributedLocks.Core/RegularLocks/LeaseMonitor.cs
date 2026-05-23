// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;
using Microsoft.Extensions.Logging;
using Nito.AsyncEx;

#pragma warning disable IDE0130
// ReSharper disable once CheckNamespace
namespace Headless.DistributedLocks;

internal sealed class LeaseMonitor : IAsyncDisposable
{
    private readonly CancellationTokenSource _disposalSource = new();
    private readonly CancellationTokenSource _handleLostSource = new();
    private readonly ILeaseHandle _leaseHandle;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger _logger;
    private readonly TimeSpan _leaseDuration;
    private readonly Lock _syncLock = new();
    private int _disposed;
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

    public LeaseMonitor(ILeaseHandle leaseHandle, TimeProvider timeProvider, ILogger logger)
    {
        _leaseHandle = Argument.IsNotNull(leaseHandle);
        _timeProvider = Argument.IsNotNull(timeProvider);
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
            _timeProvider,
            leaseHandle.MonitoringCadence,
            leaseHandle.Resource,
            leaseHandle.LockId,
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
            leaseHandle.LockId
        );

        MonitoringTask = Task.Run(() => _MonitoringLoopAsync(loopState));

        _ = MonitoringTask.ContinueWith(
            static (task, state) =>
            {
                var faultState = (FaultContinuationState)state!;

                // Fail-safe FIRST: a faulted monitor cannot keep providing liveness signals, so
                // signal loss to consumers via HandleLostToken before doing anything that could
                // itself throw (e.g., a misbehaving logger). Catch both ObjectDisposedException
                // (CTS already disposed during teardown) and AggregateException (a registered
                // HandleLostToken callback threw — Cancel() wraps the failures).
                try
                {
                    faultState.HandleLostSource.Cancel();
                }
                catch (ObjectDisposedException)
                {
                    // DisposeAsync already disposed the CTS — fault arrived during/after teardown.
                }
                catch (AggregateException aggregate)
                {
                    // A registered HandleLostToken callback threw. The original fault is still
                    // logged below; log the aggregated callback failures defensively so they are
                    // not silently swallowed.
                    try
                    {
                        faultState.Logger.LogLeaseMonitorFaulted(aggregate, faultState.Resource, faultState.LockId);
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
                    faultState.Logger.LogLeaseMonitorFaulted(task.Exception!, faultState.Resource, faultState.LockId);
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
        string LockId
    );

    private readonly AsyncAutoResetEvent _nudgeSignal;

    internal Task MonitoringTask { get; }

    public CancellationToken HandleLostToken => _handleLostSource.Token;

    public void TriggerImmediateValidation()
    {
        _nudgeSignal.Set();
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

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
                // continuation (logged + HandleLostToken cancelled). Swallow during dispose so
                // teardown cannot throw on the caller of DisposeAsync. Log defensively — a
                // misbehaving logger must not crash teardown.
                try
                {
                    _logger.LogLeaseMonitorFaulted(exception, _leaseHandle.Resource, _leaseHandle.LockId);
                }
#pragma warning disable ERP022, CA1031 // Defensive: best-effort log; ignore further logger faults during teardown.
                catch
                {
                    // Intentionally empty.
                }
#pragma warning restore ERP022, CA1031
            }

#pragma warning restore VSTHRD003
        }
        finally
        {
            _disposalSource.Dispose();
            _handleLostSource.Dispose();
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
        if (leaseLifetime > _leaseDuration && State == LeaseState.Unknown)
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
            _logger.LogLeaseMonitorValidationUnknown(exception, _leaseHandle.Resource, _leaseHandle.LockId);
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

        _logger.LogLeaseMonitorStateChanged(_leaseHandle.Resource, _leaseHandle.LockId, previousState, nextState);

        if (!shouldCancel)
        {
            return;
        }

        // Cancel asynchronously off the loop thread so that any synchronous callbacks (e.g.,
        // disposing or releasing the handle) do not deadlock awaiting the loop thread's exit.
        _ = Task.Run(() =>
        {
            try
            {
                _handleLostSource.Cancel();
            }
            catch (ObjectDisposedException)
            {
                // Disposed concurrently; treat as already-cancelled.
            }
            catch (AggregateException aggregate)
            {
                try
                {
                    _logger.LogLeaseMonitorFaulted(aggregate, _leaseHandle.Resource, _leaseHandle.LockId);
                }
#pragma warning disable ERP022, CA1031 // Defensive: best-effort log from detached cancellation task.
                catch
                {
                    // Intentionally empty.
                }
#pragma warning restore ERP022, CA1031
            }
        });
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

    internal enum LeaseState
    {
        Held,
        Renewed,
        Lost,
        Unknown,
    }

    internal interface ILeaseHandle
    {
        string Resource { get; }

        string LockId { get; }

        TimeSpan LeaseDuration { get; }

        TimeSpan MonitoringCadence { get; }

        Task<LeaseState> RenewOrValidateLeaseAsync(CancellationToken cancellationToken);
    }

    private sealed record MonitoringLoopState(
        WeakReference<LeaseMonitor> Monitor,
        AsyncAutoResetEvent NudgeSignal,
        TimeProvider TimeProvider,
        TimeSpan Cadence,
        string Resource,
        string LockId,
        ILogger Logger,
        CancellationToken DisposalToken
    );
}

internal static partial class LeaseMonitorLog
{
    [LoggerMessage(
        EventId = 30,
        EventName = "LeaseMonitorStateChanged",
        Level = LogLevel.Debug,
        Message = "Lease monitor state changed: R={Resource} Id={LockId} {PreviousState} -> {NextState}"
    )]
    public static partial void LogLeaseMonitorStateChanged(
        this ILogger logger,
        string resource,
        string lockId,
        LeaseMonitor.LeaseState previousState,
        LeaseMonitor.LeaseState nextState
    );

    [LoggerMessage(
        EventId = 31,
        EventName = "LeaseMonitorValidationUnknown",
        Level = LogLevel.Debug,
        Message = "Lease monitor validation failed transiently: R={Resource} Id={LockId}"
    )]
    public static partial void LogLeaseMonitorValidationUnknown(
        this ILogger logger,
        Exception exception,
        string resource,
        string lockId
    );

    [LoggerMessage(
        EventId = 32,
        EventName = "LeaseMonitorFaulted",
        Level = LogLevel.Error,
        Message = "Lease monitor loop faulted: R={Resource} Id={LockId}"
    )]
    public static partial void LogLeaseMonitorFaulted(
        this ILogger logger,
        Exception exception,
        string resource,
        string lockId
    );
}
