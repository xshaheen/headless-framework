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
    private Task? _cancellationTask;
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

        NudgeSignal = new AsyncAutoResetEvent();

        var loopState = new MonitoringLoopState(
            new WeakReference<LeaseMonitor>(this),
            NudgeSignal,
            _timeProvider,
            leaseHandle.MonitoringCadence,
            leaseHandle.Resource,
            leaseHandle.LockId,
            _logger,
            _disposalSource.Token
        );

        MonitoringTask = Task.Factory.StartNew(
            static state => _MonitoringLoopAsync((MonitoringLoopState)state!),
            loopState,
            CancellationToken.None,
            TaskCreationOptions.DenyChildAttach,
            TaskScheduler.Default
        ).Unwrap();

        _ = MonitoringTask.ContinueWith(
            static (task, state) =>
            {
                var loopState = (MonitoringLoopState)state!;
                loopState.Logger.LogLeaseMonitorFaulted(task.Exception!, loopState.Resource, loopState.LockId);
            },
            loopState,
            CancellationToken.None,
            TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default
        );
    }

    internal AsyncAutoResetEvent NudgeSignal { get; }

    internal Task MonitoringTask { get; }

    public CancellationToken HandleLostToken => _handleLostSource.Token;

    public void TriggerImmediateValidation()
    {
        NudgeSignal.Set();
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
            NudgeSignal.Set();
#pragma warning disable VSTHRD003 // DisposeAsync is the owner that drains the background monitor task.
            await MonitoringTask.ConfigureAwait(false);

            if (_cancellationTask is not null)
            {
                await _cancellationTask.ConfigureAwait(false);
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
        if (leaseLifetime > _leaseDuration)
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
        Task? cancellationTask = null;

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

            if (nextState == LeaseState.Lost)
            {
                var handleLostSource = _handleLostSource;
                cancellationTask = Task.Run(
                    async () => await handleLostSource.CancelAsync().ConfigureAwait(false),
                    CancellationToken.None
                );
                _cancellationTask = cancellationTask;
            }
        }

        if (cancellationTask is not null)
        {
            _ = cancellationTask.ContinueWith(
                static task => _ = task.Exception,
                CancellationToken.None,
                TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously,
                TaskScheduler.Default
            );
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
