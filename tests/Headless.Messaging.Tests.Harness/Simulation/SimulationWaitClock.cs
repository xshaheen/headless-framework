// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;

namespace Tests.Simulation;

/// <summary>
/// The clock a test's own waits run on against a <see cref="SimulatedBroker"/>: a receive timeout, a window in which
/// nothing may arrive, a pause between publishes. Simulated time moves only while such a wait is pending, and each step
/// waits until the broker is quiescent, so a test body written for a real broker's wall-clock windows runs against the
/// simulator in no real time, and a step never overtakes a delivery that was already due.
/// </summary>
/// <remarks>
/// <para>
/// It reads and moves the broker's <see cref="SimulationClock"/>. While a wait created on it is pending, it repeatedly
/// lets the broker settle and then moves the clock to the nearest pending wait of any kind: a client's retry time comes
/// due before a test's receive timeout, so the client reconnects and delivers before the test gives up.
/// </para>
/// <para>
/// Time does not move between a test's waits, so a client cut off from the broker retries only while the test waits on
/// something. Use one per broker, and do not advance the broker's clock elsewhere while it runs.
/// </para>
/// </remarks>
[PublicAPI]
public sealed class SimulationWaitClock : TimeProvider, IAsyncDisposable
{
    private readonly SimulatedBroker _broker;
    private readonly Lock _gate = new();
    private readonly CancellationTokenSource _stopping = new();
    private readonly Task _pump;
    private TaskCompletionSource _armed = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private int _pendingWaits;
    private int _disposed;

    /// <summary>Creates a clock for the test waits on <paramref name="broker"/> and starts moving its time.</summary>
    public SimulationWaitClock(SimulatedBroker broker)
    {
        _broker = Argument.IsNotNull(broker);
        _pump = Task.Run(_PumpAsync);
    }

    public override DateTimeOffset GetUtcNow() => _broker.Clock.GetUtcNow();

    public override long GetTimestamp() => _broker.Clock.GetTimestamp();

    public override long TimestampFrequency => _broker.Clock.TimestampFrequency;

    public override TimeZoneInfo LocalTimeZone => _broker.Clock.LocalTimeZone;

    public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
    {
        Argument.IsNotNull(callback);

        var timer = new WaitTimer(this, callback, state);

        // Counted before the inner timer exists, because a zero due time fires inside CreateTimer itself.
        timer.Arm(dueTime, period);
        timer.Inner = _broker.Clock.CreateTimer(static self => ((WaitTimer)self!).Fire(), timer, dueTime, period);

        return timer;
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        await _stopping.CancelAsync().ConfigureAwait(false);
        await _pump.ConfigureAwait(false);
        _stopping.Dispose();
    }

    private async Task _PumpAsync()
    {
        var token = _stopping.Token;

        try
        {
            while (true)
            {
                Task armed;
                lock (_gate)
                {
                    armed = _pendingWaits > 0 ? Task.CompletedTask : _armed.Task;
                }

                await armed.WaitAsync(token).ConfigureAwait(false);
                await _broker.WhenQuiescentAsync(token).ConfigureAwait(false);

                lock (_gate)
                {
                    if (_pendingWaits == 0)
                    {
                        continue;
                    }
                }

                var pending = _broker.Clock.PendingWaitsArmedSince(0);
                var step = pending.Count == 0 || pending[0] < TimeSpan.Zero ? TimeSpan.Zero : pending[0];
                _broker.Clock.Advance(step);

                // A client woken by the advance looks parked until it polls again; counting the advance as a broker
                // change makes quiescence wait for every client to look at the new time first.
                _broker.Signal();
            }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            // Disposed.
        }
    }

    private void _Arm(WaitTimer timer, bool armed)
    {
        lock (_gate)
        {
            if (timer.Armed == armed)
            {
                return;
            }

            timer.Armed = armed;
            _pendingWaits += armed ? 1 : -1;

            if (armed && _pendingWaits == 1)
            {
                var signal = _armed;
                _armed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                signal.TrySetResult();
            }
        }
    }

    /// <summary>A timer of a test's wait, counted as pending from when it is armed until it fires or is disposed.</summary>
    private sealed class WaitTimer(SimulationWaitClock clock, TimerCallback callback, object? state) : ITimer
    {
        private TimeSpan _period;

        // Guarded by the clock's gate.
        public bool Armed { get; set; }

        public ITimer? Inner { get; set; }

        public void Arm(TimeSpan dueTime, TimeSpan period)
        {
            _period = period;
            clock._Arm(this, dueTime != Timeout.InfiniteTimeSpan);
        }

        public bool Change(TimeSpan dueTime, TimeSpan period)
        {
            Arm(dueTime, period);
            return Inner!.Change(dueTime, period);
        }

        public void Fire()
        {
            if (_period == Timeout.InfiniteTimeSpan || _period == TimeSpan.Zero)
            {
                clock._Arm(this, armed: false);
            }

            callback(state);
        }

        public void Dispose()
        {
            clock._Arm(this, armed: false);
            Inner?.Dispose();
        }

        public ValueTask DisposeAsync()
        {
            Dispose();
            return ValueTask.CompletedTask;
        }
    }
}
