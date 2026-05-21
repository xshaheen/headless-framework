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
    private LeaseState State { get; set; } = LeaseState.Held;

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

        MonitoringTask = Task.Run(() => _MonitoringLoopAsync(loopState));

        _ = MonitoringTask.ContinueWith(
            (task, state) =>
            {
                var loopState = (MonitoringLoopState)state!;

                // Fail-safe FIRST: a faulted monitor cannot keep providing liveness signals, so
                // signal loss to consumers via HandleLostToken before doing anything that could
                // itself throw (e.g., a misbehaving logger).
                try
                {
                    _handleLostSource.Cancel();
                }
                catch (ObjectDisposedException)
                {
                    // DisposeAsync already disposed the CTS — fault arrived during/after teardown.
                }

                try
                {
                    loopState.Logger.LogLeaseMonitorFaulted(task.Exception!, loopState.Resource, loopState.LockId);
                }
#pragma warning disable ERP022, CA1031 // Defensive: a faulting logger must not propagate from this continuation.
                catch
                {
                    // Intentionally empty.
                }
#pragma warning restore ERP022, CA1031
            },
            loopState,
            CancellationToken.None,
            TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default
        );
    }

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
        // Safety-net self-promotion: only when prior probes were transient (Unknown).
        // A confirmed Held/Renewed from storage MUST NOT trip the safety net — that would
        // produce a false-positive Lost in polling mode where Held repeats indefinitely
        // and leaseTimestamp is only reset on Renewed.
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

        _SetState(nextState);

        return nextState;
    }

    private void _SetState(LeaseState nextState)
    {
        bool shouldCancel;

        lock (_syncLock)
        {
            if (State == LeaseState.Lost)
            {
                return;
            }

            if (State == nextState)
            {
                return;
            }

            _logger.LogLeaseMonitorStateChanged(_leaseHandle.Resource, _leaseHandle.LockId, State, nextState);
            State = nextState;
            shouldCancel = nextState == LeaseState.Lost;
        }

        if (!shouldCancel)
        {
            return;
        }

        // Cancel synchronously rather than via Task.Run. A HandleLostToken callback that disposes
        // the handle would otherwise self-await via DisposeAsync → await _cancellationTask, where
        // _cancellationTask is the Task.Run running the callback that's calling DisposeAsync.
        // Cancel() is contracted (per IDistributedLock.HandleLostToken docs) as observability;
        // consumers MUST NOT run blocking work inline in callbacks.
        try
        {
            _handleLostSource.Cancel();
        }
        catch (ObjectDisposedException)
        {
            // Disposed concurrently; treat as already-cancelled.
        }
    }

    private static async Task _MonitoringLoopAsync(MonitoringLoopState state)
    {
        var leaseTimestamp = state.TimeProvider.GetTimestamp();

        while (!state.DisposalToken.IsCancellationRequested)
        {
            using var cadenceSource = state.TimeProvider.CreateCancellationTokenSource(state.Cadence);
            using var linkedSource = CancellationTokenSource.CreateLinkedTokenSource(
                cadenceSource.Token,
                state.DisposalToken
            );

            await state.NudgeSignal.SafeWaitAsync(linkedSource.Token).ConfigureAwait(false);

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

            if (nextState == LeaseState.Renewed)
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
        EventId = 1,
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
        EventId = 2,
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
        EventId = 3,
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
