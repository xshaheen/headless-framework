// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Data;
using System.Data.Common;
using System.Diagnostics;
using Headless.Checks;

#pragma warning disable IDE0130 // ReSharper disable once CheckNamespace
namespace Headless.DistributedLocks;

/// <summary>
/// Implements keepalive for a <see cref="DatabaseConnection"/> (important for providers that idle-timeout
/// connections) and active connection-death monitoring used to back the lock handle's connection-lost token.
/// </summary>
/// <remarks>
/// The monitor holds a <see cref="WeakReference{T}"/> to its connection so the background worker cannot keep an
/// abandoned connection (and therefore an abandoned lock) alive: if the consumer drops the handle without disposing,
/// the worker observes the dead weak reference and exits, allowing the connection to be GC-collected and the lock to
/// release server-side. The <see cref="DbConnection.StateChange"/> event covers clean disconnects; the bounded
/// command timeout on every probe covers silent half-open connections (a network drop with no RST).
/// </remarks>
internal sealed class ConnectionMonitor : IAsyncDisposable
{
    /// <summary>Default bounded command timeout (seconds) for keepalive/monitoring probes.</summary>
    internal const int DefaultMonitoringCommandTimeoutSeconds = 10;

    /// <summary>Cadence of the long-running monitoring probe (the server-side sleep).</summary>
    private static readonly TimeSpan _MonitoringProbeCadence = TimeSpan.FromMinutes(1);

    private readonly WeakReference<DatabaseConnection> _weakConnection;
    private readonly StateChangeEventHandler? _stateChangedHandler;
    private readonly bool _isExternallyOwnedConnection;
    private readonly TimeProvider _timeProvider;
    private readonly int _monitoringCommandTimeoutSeconds;

    // Limits concurrent queries against the connection. SemaphoreSlim (not Nito.AsyncEx.AsyncLock) is used here
    // because the monitor needs a zero-wait try-acquire for keepalive and a timed try-acquire for the FIFO retry in
    // AcquireConnectionLockAsync — neither of which AsyncLock exposes.
#pragma warning disable CA2213 // Disposed explicitly at the end of _StopOrDisposeAsync (after the worker drains).
    private readonly SemaphoreSlim _connectionLock = new(initialCount: 1, maxCount: 1);
#pragma warning restore CA2213

    private TimeSpan _keepaliveCadence = Timeout.InfiniteTimeSpan;
    private State _state;
    private Dictionary<MonitoringHandle, CancellationTokenSource>? _monitoringHandleRegistrations;

#pragma warning disable CA2213 // Lifecycle is rotated by _FireStateChangedNoLock and drained/disposed in _StopOrDisposeAsync.
    private CancellationTokenSource? _monitorStateChangedTokenSource;
#pragma warning restore CA2213

    private Task _monitoringWorkerTask = Task.CompletedTask;

    /// <summary>
    /// Initializes a monitor for <paramref name="connection"/>.
    /// </summary>
    /// <param name="connection">The database connection to monitor and keepalive.</param>
    /// <param name="timeProvider">Clock used for keepalive delays.</param>
    /// <param name="monitoringCommandTimeoutSeconds">Bounded command timeout for keepalive and monitoring probes; must be positive.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="connection"/> or <paramref name="timeProvider"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="monitoringCommandTimeoutSeconds"/> is not positive.</exception>
    public ConnectionMonitor(
        DatabaseConnection connection,
        TimeProvider timeProvider,
        int monitoringCommandTimeoutSeconds = DefaultMonitoringCommandTimeoutSeconds
    )
    {
        Argument.IsNotNull(connection);
        _timeProvider = Argument.IsNotNull(timeProvider);
        _monitoringCommandTimeoutSeconds = Argument.IsPositive(monitoringCommandTimeoutSeconds);

        _weakConnection = new WeakReference<DatabaseConnection>(connection);
        _isExternallyOwnedConnection = connection.IsExternallyOwned;

        // Stopped (not AutoStopped) here so the state-change handler will not cause a start.
        _state = connection.CanExecuteQueries ? State.Idle : State.Stopped;
        Debug.Assert(_state == State.Stopped || _isExternallyOwnedConnection);

        _stateChangedHandler = _OnConnectionStateChanged;
        connection.InnerConnection.StateChange += _stateChangedHandler;
    }

    /// <summary>Protects all mutable state. The weak reference is a stable, otherwise-unused lock object.</summary>
    private object Lock => _weakConnection;

    private bool HasRegisteredMonitoringHandlesNoLock =>
        (_monitoringHandleRegistrations?.Count).GetValueOrDefault() != 0;

    /// <summary>
    /// Acquires the internal connection semaphore, which gates concurrent query execution on the
    /// underlying connection. If the monitor is actively probing (connection in monitoring mode), it fires
    /// a state-change interrupt to cancel the probe and let this acquirer in. Retries in a timed loop
    /// to avoid live-locking the monitor worker between fire-state-changed and the next probe.
    /// </summary>
    /// <param name="cancellationToken">Token used to cancel the wait for the connection lock.</param>
    /// <returns>An <see cref="IDisposable"/> whose <c>Dispose</c> releases the semaphore, or <see langword="null"/> when the caller is a monitoring query that already holds it.</returns>
    /// <exception cref="OperationCanceledException">Thrown when <paramref name="cancellationToken"/> is cancelled while waiting.</exception>
    public async ValueTask<IDisposable?> AcquireConnectionLockAsync(CancellationToken cancellationToken)
    {
        while (true)
        {
            lock (Lock)
            {
                // If we're monitoring then the connection is almost constantly in use (the long sleep probe). Fire
                // state changed to cancel that probe and let this acquirer in.
                if (_state == State.Active && HasRegisteredMonitoringHandlesNoLock)
                {
                    _FireStateChangedNoLock();
                }
            }

            // A timed try-acquire (rather than an unbounded wait) so a contended monitor probe is retried, giving the
            // worker loop a chance to release the lock between fire-state-changed and the next probe.
            if (await _connectionLock.WaitAsync(TimeSpan.FromSeconds(2), cancellationToken).ConfigureAwait(false))
            {
                return new SemaphoreReleaser(_connectionLock);
            }
        }
    }

    /// <summary>
    /// Sets the keepalive cadence and starts or wakes the monitor worker if needed. Only valid for
    /// internally-owned connections; keepalive is meaningless on externally-owned ones.
    /// </summary>
    /// <param name="keepaliveCadence">
    /// Interval between keepalive pings. <see cref="Timeout.InfiniteTimeSpan"/> disables keepalive.
    /// A shorter cadence than the current one fires a state-change interrupt to wake a sleeping worker.
    /// </param>
    /// <exception cref="InvalidOperationException">Thrown when called on an externally-owned connection or when the monitor is already disposed.</exception>
    public void SetKeepaliveCadence(TimeSpan keepaliveCadence)
    {
        Ensure.True(!_isExternallyOwnedConnection, "Cannot run keepalive on an externally-owned connection.");

        lock (Lock)
        {
            Ensure.True(_state != State.Disposed, "Connection monitor is disposed.");

            var originalKeepaliveCadence = _keepaliveCadence;
            _keepaliveCadence = keepaliveCadence;

            if (
                !_StartMonitorWorkerIfNeededNoLock()
                && _state == State.Active
                && !HasRegisteredMonitoringHandlesNoLock
                && TimeSpanCadence.CompareWithInfinite(keepaliveCadence, originalKeepaliveCadence) < 0
            )
            {
                // An active worker is already performing keepalive on a longer cadence. It is likely asleep, so fire
                // state changed to wake it and pick up the shorter cadence.
                _FireStateChangedNoLock();
            }
        }
    }

    /// <summary>
    /// Returns a monitoring handle whose <see cref="IDatabaseConnectionMonitoringHandle.ConnectionLostToken"/> is
    /// cancelled if the connection is observed to die. While any live handle is registered, the monitor worker
    /// runs a long-sleeping probe against the connection. Disposing the handle unregisters it.
    /// </summary>
    /// <remarks>
    /// Returns an already-cancelled handle when the connection is already closed, and a no-op handle when the
    /// connection does not support state-change events. Thread-safe.
    /// </remarks>
    /// <returns>A live <see cref="IDatabaseConnectionMonitoringHandle"/> whose <c>ConnectionLostToken</c> fires on connection loss.</returns>
    /// <exception cref="ObjectDisposedException">Thrown when the monitor has already been disposed.</exception>
    public IDatabaseConnectionMonitoringHandle GetMonitoringHandle()
    {
        lock (Lock)
        {
            // Called via non-thread-safe paths, so a true dispose check is appropriate; it only reaches a caller that
            // is using non-thread-safe APIs concurrently.
            Ensure.NotDisposed(_state == State.Disposed, this);

            // The connection is already closed; we'll never see a close state-change, so return an already-cancelled
            // handle.
            if (_state is State.AutoStopped or State.Stopped)
            {
                return new AlreadyCanceledHandle();
            }

            // The connection does not support state monitoring; we can't produce a real monitoring handle.
            if (_stateChangedHandler is null)
            {
                return NullHandle.Instance;
            }

            var hadRegisteredMonitoringHandles = HasRegisteredMonitoringHandlesNoLock;

            var connectionLostTokenSource = new CancellationTokenSource();
            var handle = new MonitoringHandle(this, connectionLostTokenSource.Token);
            (_monitoringHandleRegistrations ??= []).Add(handle, connectionLostTokenSource);

            if (!_StartMonitorWorkerIfNeededNoLock() && !hadRegisteredMonitoringHandles && _state == State.Active)
            {
                // An active worker was doing keepalive (not monitoring) and is likely asleep. Wake it so it switches
                // over to monitoring.
                _FireStateChangedNoLock();
            }

            return handle;
        }
    }

    private void _ReleaseMonitoringHandle(MonitoringHandle handle)
    {
        lock (Lock)
        {
            if (_monitoringHandleRegistrations!.TryGetValue(handle, out var cancellationTokenSource))
            {
                _monitoringHandleRegistrations.Remove(handle);
                cancellationTokenSource.Dispose();

                // If we've removed the last reason to monitor, fire state changed to stop the monitoring probe. Without
                // this, the next query that takes the connection lock would not think we are monitoring, would not fire
                // state changed, and would hang waiting for the monitoring probe to complete.
                if (_monitoringHandleRegistrations.Count == 0 && _state == State.Active)
                {
                    _FireStateChangedNoLock();
                }
            }
        }
    }

    private void _OnConnectionStateChanged(object sender, StateChangeEventArgs args)
    {
        if (args is { OriginalState: ConnectionState.Open, CurrentState: not ConnectionState.Open })
        {
            lock (Lock)
            {
                if (_state is State.Idle or State.Active)
                {
                    _state = State.AutoStopped;
                    _CloseOrCancelMonitoringHandleRegistrationsNoLock(isCancel: true);
                }

                Debug.Assert(!HasRegisteredMonitoringHandlesNoLock);
            }
        }
        else if (args is { OriginalState: not ConnectionState.Open, CurrentState: ConnectionState.Open })
        {
            lock (Lock)
            {
                if (_state == State.AutoStopped)
                {
                    _StartNoLock();
                }
            }
        }
    }

    /// <summary>
    /// Transitions the monitor from the initial <c>Stopped</c> state to <c>Idle</c> and starts the
    /// background worker when keepalive or monitoring handles are registered. Must be called after
    /// <see cref="DatabaseConnection.OpenAsync"/> for internally-owned connections.
    /// </summary>
    /// <exception cref="InvalidOperationException">Thrown when called on an externally-owned connection or when the monitor is not in the <c>Stopped</c> state.</exception>
    public void Start()
    {
        Ensure.True(!_isExternallyOwnedConnection, "Cannot start monitoring an externally-owned connection.");

        lock (Lock)
        {
            Ensure.True(_state == State.Stopped, "Connection monitor is not in the stopped state.");
            _StartNoLock();
        }
    }

    private void _StartNoLock()
    {
        _state = State.Idle;
        _StartMonitorWorkerIfNeededNoLock();
    }

    /// <summary>
    /// Stops the background worker and waits for it to drain. Does not cancel registered monitoring handles
    /// (a stop represents clean teardown, not connection loss). Only valid for internally-owned connections.
    /// </summary>
    /// <exception cref="InvalidOperationException">Thrown when called on an externally-owned connection or when the monitor is already disposed.</exception>
    public ValueTask StopAsync() => _StopOrDisposeAsync(isDispose: false);

    /// <summary>
    /// Stops the background worker, unregisters the state-change handler, disposes internal resources,
    /// and waits for the worker to drain. Monitoring handles are closed without cancellation.
    /// </summary>
    public ValueTask DisposeAsync() => _StopOrDisposeAsync(isDispose: true);

    private async ValueTask _StopOrDisposeAsync(bool isDispose)
    {
        Task? task;
        CancellationTokenSource? stateChangedTokenSource;

        lock (Lock)
        {
            if (isDispose)
            {
                _state = State.Disposed;
            }
            else
            {
                Ensure.True(!_isExternallyOwnedConnection, "Cannot stop monitoring an externally-owned connection.");
                Ensure.True(_state != State.Disposed, "Connection monitor is disposed.");
                _state = State.Stopped;
            }

            // Clear any registered monitoring handles. We do NOT cancel them here: a stop indicates proper disposal
            // rather than loss of the connection.
            _CloseOrCancelMonitoringHandleRegistrationsNoLock(isCancel: false);

            task = _monitoringWorkerTask;

            // Capture and clear the token source so we can cancel it outside the lock. Cancelling is safe regardless
            // of ordering: the state was already set above, which the loop re-checks if it runs continuations on the
            // cancel thread.
            stateChangedTokenSource = _monitorStateChangedTokenSource;
            _monitorStateChangedTokenSource = null;

            if (isDispose && _stateChangedHandler is not null && _weakConnection.TryGetTarget(out var connection))
            {
                connection.InnerConnection.StateChange -= _stateChangedHandler;
            }
        }

        if (stateChangedTokenSource is not null)
        {
            await stateChangedTokenSource.CancelAsync().ConfigureAwait(false);
            stateChangedTokenSource.Dispose();
        }

        if (task is not null)
        {
#pragma warning disable VSTHRD003 // We own this task and are draining the background monitor worker.
            await task.ConfigureAwait(false);
#pragma warning restore VSTHRD003
        }

        if (isDispose)
        {
            _connectionLock.Dispose();
        }
    }

    private void _CloseOrCancelMonitoringHandleRegistrationsNoLock(bool isCancel)
    {
        Debug.Assert(_state is State.AutoStopped or State.Stopped or State.Disposed);

        if (_monitoringHandleRegistrations is null)
        {
            return;
        }

        foreach (var registration in _monitoringHandleRegistrations)
        {
            var cancellationTokenSource = registration.Value;

            if (isCancel)
            {
                // Cancel on a background thread in case a registered callback hangs or throws.
                _ = Task.Run(
                    () =>
                    {
                        try
                        {
                            cancellationTokenSource.Cancel();
                        }
                        finally
                        {
                            cancellationTokenSource.Dispose();
                        }
                    },
                    CancellationToken.None
                );
            }
            else
            {
                cancellationTokenSource.Dispose();
            }
        }

        _monitoringHandleRegistrations.Clear();
    }

    private bool _StartMonitorWorkerIfNeededNoLock()
    {
        Debug.Assert(_state != State.Disposed);

        // never monitor external connections
        if (_isExternallyOwnedConnection)
        {
            return false;
        }

        // Active means a worker already exists; any non-Idle state means we're not supposed to be running one.
        if (_state != State.Idle)
        {
            return false;
        }

        // nothing to do
        if (_keepaliveCadence == Timeout.InfiniteTimeSpan && !HasRegisteredMonitoringHandlesNoLock)
        {
            return false;
        }

        _monitorStateChangedTokenSource = new CancellationTokenSource();

        // Chain on the previous worker task to avoid concurrency when a previous worker is spinning down. Rapid state
        // changes can queue several continuations, but when the active one stops the rest follow immediately.
        _monitoringWorkerTask = _monitoringWorkerTask
            .ContinueWith(
                static (_, state) => ((ConnectionMonitor)state!)._MonitorWorkerLoopAsync(),
                state: this,
                CancellationToken.None,
                TaskContinuationOptions.None,
                TaskScheduler.Default
            )
            .Unwrap();
        _state = State.Active;

        return true;
    }

    private void _FireStateChangedNoLock()
    {
        var monitorStateChangedTokenSource = _monitorStateChangedTokenSource!;
        _monitorStateChangedTokenSource = new CancellationTokenSource();

        // Cancel asynchronously: the Cancel() thread can otherwise run continuations inside the monitoring loop.
        // Setting the new source before cancelling the old one already avoids the worst of that, but doing the cancel
        // off-thread keeps this method fast and easy to reason about.
        _ = Task.Run(
            () =>
            {
                try
                {
                    monitorStateChangedTokenSource.Cancel();
                }
                finally
                {
                    monitorStateChangedTokenSource.Dispose();
                }
            },
            CancellationToken.None
        );
    }

    private async Task _MonitorWorkerLoopAsync()
    {
        while (await _TryKeepaliveOrMonitorAsync().ConfigureAwait(false))
        {
            // keep going
        }
    }

    private async Task<bool> _TryKeepaliveOrMonitorAsync()
    {
        TimeSpan keepaliveCadence;
        bool isMonitoring;
        CancellationToken stateChangedToken;

        lock (Lock)
        {
            if (_state != State.Active)
            {
                return false;
            }

            keepaliveCadence = _keepaliveCadence;
            isMonitoring = HasRegisteredMonitoringHandlesNoLock;
            stateChangedToken = _monitorStateChangedTokenSource!.Token;
        }

        return await (
            isMonitoring
                ? _DoMonitoringAsync(stateChangedToken)
                : _DoKeepaliveAsync(keepaliveCadence, stateChangedToken)
        ).ConfigureAwait(false);
    }

    private async Task<bool> _DoMonitoringAsync(CancellationToken cancellationToken)
    {
        if (!_weakConnection.TryGetTarget(out var connection))
        {
            return false;
        }

        // No token here: this should finish quickly and we don't want it to throw.
        await _connectionLock.WaitAsync(CancellationToken.None).ConfigureAwait(false);

        try
        {
            // A long server-side sleep is the monitoring probe. It is cancelled (via the state-changed token) when an
            // acquirer needs the connection or when monitoring is no longer needed. The bounded command timeout the
            // executor applies guarantees a silently-dead connection surfaces as a fault rather than hanging forever.
            await _SuppressAsync(
                    connection.SleepAsync(
                        sleepTime: _MonitoringProbeCadence,
                        executor: (command, token) =>
                        {
                            command.SetExactTimeoutSeconds(_monitoringCommandTimeoutSeconds);

                            return command.ExecuteNonQueryAsync(isConnectionMonitoringQuery: true, token);
                        },
                        cancellationToken: cancellationToken
                    )
                )
                .ConfigureAwait(false);
        }
        finally
        {
            _connectionLock.Release();
        }

        return true;
    }

    private async Task<bool> _DoKeepaliveAsync(TimeSpan keepaliveCadence, CancellationToken stateChangedToken)
    {
        await _SuppressAsync(_timeProvider.Delay(keepaliveCadence, stateChangedToken)).ConfigureAwait(false);

        if (stateChangedToken.IsCancellationRequested)
        {
            return true;
        }

        // Retrieve only after the delay so we don't keep the connection referenced for longer than needed.
        if (!_weakConnection.TryGetTarget(out var connection))
        {
            return false;
        }

        // Zero-wait try-acquire: if the connection is in use, someone is already querying it and no keepalive is
        // needed. Zero timeout means we don't pass the token, avoiding cancellation-exception handling.
        if (await _connectionLock.WaitAsync(TimeSpan.Zero, CancellationToken.None).ConfigureAwait(false))
        {
            try
            {
                using var command = connection.CreateCommand();
                command.SetCommandText("SELECT 0 /* Headless distributed-lock connection keepalive */");
                command.SetExactTimeoutSeconds(_monitoringCommandTimeoutSeconds);

                // Fast and non-blocking; we don't bother cancelling it.
#pragma warning disable VSTHRD003 // The task is started here by ExecuteNonQueryAsync, not awaited from elsewhere.
                await _SuppressAsync(
                        command.ExecuteNonQueryAsync(isConnectionMonitoringQuery: true, CancellationToken.None).AsTask()
                    )
                    .ConfigureAwait(false);
#pragma warning restore VSTHRD003
            }
            finally
            {
                _connectionLock.Release();
            }
        }

        return true;
    }

    /// <summary>Awaits <paramref name="task"/>, swallowing any fault or cancellation; the worker loop must not throw.</summary>
    private static async Task _SuppressAsync(Task task)
    {
        try
        {
#pragma warning disable VSTHRD003 // The caller always passes a task it just started against this connection.
            await task.ConfigureAwait(false);
#pragma warning restore VSTHRD003
        }
#pragma warning disable CA1031, ERP022 // The worker loop intentionally ignores probe failures; loss is surfaced via state change/handle cancellation.
        catch
        {
            // Intentionally empty.
        }
#pragma warning restore CA1031, ERP022
    }

    private sealed class SemaphoreReleaser(SemaphoreSlim semaphore) : IDisposable
    {
        private int _released;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _released, 1) == 0)
            {
                semaphore.Release();
            }
        }
    }

    private sealed class MonitoringHandle(ConnectionMonitor monitor, CancellationToken connectionLostToken)
        : IDatabaseConnectionMonitoringHandle
    {
#pragma warning disable CA2213 // Back-reference to the owning monitor; the handle does not own the monitor's lifetime.
        private ConnectionMonitor? _monitor = monitor;
#pragma warning restore CA2213

        public CancellationToken ConnectionLostToken =>
            Volatile.Read(ref _monitor) is not null ? connectionLostToken : throw new ObjectDisposedException("handle");

        public void Dispose()
        {
            Interlocked.Exchange(ref _monitor, null)?._ReleaseMonitoringHandle(this);
        }
    }

    private sealed class AlreadyCanceledHandle : IDatabaseConnectionMonitoringHandle
    {
        private readonly CancellationTokenSource _cancellationTokenSource = new();

        public AlreadyCanceledHandle()
        {
#pragma warning disable MA0045 // Do not use blocking calls, even when the calling method must become async
            _cancellationTokenSource.Cancel();
#pragma warning restore MA0045
        }

        public CancellationToken ConnectionLostToken => _cancellationTokenSource.Token;

        public void Dispose()
        {
            _cancellationTokenSource.Dispose();
        }
    }

    private sealed class NullHandle : IDatabaseConnectionMonitoringHandle
    {
        public static readonly NullHandle Instance = new();

        private NullHandle() { }

        public CancellationToken ConnectionLostToken => CancellationToken.None;

        public void Dispose() { }
    }

    /// <summary>Lifecycle states of the connection monitor.</summary>
    private enum State : byte
    {
        /// <summary>Monitor started but no background worker is running (no keepalive cadence and no monitoring handles).</summary>
        Idle,

        /// <summary>Background worker is running.</summary>
        Active,

        /// <summary>Connection closed spontaneously (state-change event); may restart if the connection reopens.</summary>
        AutoStopped,

        /// <summary>Explicitly stopped via <see cref="StopAsync"/>.</summary>
        Stopped,

        /// <summary>Disposed; no further use allowed.</summary>
        Disposed,
    }
}
