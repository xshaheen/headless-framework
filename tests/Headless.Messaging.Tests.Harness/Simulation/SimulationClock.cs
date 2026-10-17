// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;

namespace Tests.Simulation;

/// <summary>
/// The one clock a simulation runs on. Time moves only when a test calls <see cref="Advance"/>, and the clock keeps a
/// record of every wait armed on it, so a run stuck on simulated time can be told apart from slow work.
/// </summary>
/// <remarks>
/// <para>
/// Every <c>Task.Delay</c>, <c>WaitAsync</c>, cancellation timeout, and <c>PeriodicTimer</c> on a
/// <see cref="TimeProvider"/> goes through <see cref="CreateTimer"/>, so the clock sees each wait the code under test
/// arms and can report when it is due.
/// </para>
/// <para>
/// It keeps its own timers instead of wrapping a fake clock. A timer's due time is taken from the same reading of the
/// time, under the same gate, that <see cref="Advance"/> moves. A wrapper reads the time once to record a due time and
/// the wrapped clock reads it again to schedule the timer, so an advance on another thread between the two reads leaves
/// the timer recorded as due before it fires, and a driver that steps to the nearest wait then steps by zero forever.
/// </para>
/// <para>
/// Timer callbacks run on the advancing thread, outside the gate, earliest due first, the way
/// <c>FakeTimeProvider</c> runs them: one thread fires at a time, and an advance that arrives while another thread is
/// firing leaves its timers to that thread.
/// </para>
/// </remarks>
/// <param name="start">The simulated instant the clock starts at.</param>
[PublicAPI]
public sealed class SimulationClock(DateTimeOffset start) : TimeProvider
{
    private readonly Lock _gate = new();
    private readonly List<SimulatedTimer> _timers = [];
    private readonly List<Deadline> _deadlines = [];
    private DateTimeOffset _now = start;
    private long _armSequence;
    private int _firing;

    /// <summary>Creates a clock that starts at midnight UTC on 1 January 2026.</summary>
    public SimulationClock()
        : this(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero)) { }

    /// <summary>
    /// Gets a mark that moves each time a wait is armed on this clock. <see cref="PendingWaitsArmedSince"/> reads only
    /// the waits armed after a mark, so a caller can ask what a step it started is waiting for.
    /// </summary>
    public long WaitMark => Interlocked.Read(ref _armSequence);

    public override DateTimeOffset GetUtcNow()
    {
        lock (_gate)
        {
            return _now;
        }
    }

    public override long GetTimestamp() => GetUtcNow().Ticks;

    public override long TimestampFrequency => TimeSpan.TicksPerSecond;

    public override TimeZoneInfo LocalTimeZone => TimeZoneInfo.Utc;

    /// <summary>Moves the clock forward, firing every timer and completing every deadline it passes.</summary>
    /// <param name="delta">How far to move; zero fires only what is already due.</param>
    public void Advance(TimeSpan delta)
    {
        Argument.IsPositiveOrZero(delta);

        lock (_gate)
        {
            _now += delta;
        }

        // The callbacks run the code under test inline, so the gate is not held across them: a continuation that arms
        // a new wait must not block on this advance.
        _FireDueTimers();

        List<Deadline> reached;
        lock (_gate)
        {
            var now = _now;
            reached = _deadlines.FindAll(deadline => deadline.At <= now);
            _deadlines.RemoveAll(deadline => deadline.At <= now);
        }

        foreach (var deadline in reached)
        {
            deadline.Cancellation.Dispose();
            deadline.Reached.TrySetResult();
        }
    }

    /// <summary>Completes once the clock reaches <paramref name="deadline"/>, at once when it already has.</summary>
    /// <param name="deadline">The simulated instant to wait for.</param>
    /// <param name="cancellationToken">Abandons the wait.</param>
    public Task WhenReachedAsync(DateTimeOffset deadline, CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            // Read under the gate: an advance publishes its time before it collects deadlines, so a deadline is either
            // seen as reached here or registered before that collection.
            if (_now >= deadline)
            {
                return Task.CompletedTask;
            }

            return _Register(deadline, cancellationToken);
        }
    }

    /// <summary>Completes once the clock has moved <paramref name="duration"/> past the moment of the call.</summary>
    /// <param name="duration">The simulated time to wait.</param>
    /// <param name="cancellationToken">Abandons the wait.</param>
    public Task DelayAsync(TimeSpan duration, CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            // Measured from the time read under the gate, so an advance racing the call cannot be missed.
            var deadline = _now + duration;
            return duration <= TimeSpan.Zero ? Task.CompletedTask : _Register(deadline, cancellationToken);
        }
    }

    /// <summary>
    /// Lists how far ahead each pending wait armed after <paramref name="mark"/> is due, nearest first: timers the code
    /// under test created or re-armed, and deadlines registered through this clock.
    /// </summary>
    /// <param name="mark">A value read from <see cref="WaitMark"/>.</param>
    public IReadOnlyList<TimeSpan> PendingWaitsArmedSince(long mark)
    {
        lock (_gate)
        {
            var now = _now;
            return _timers
                .Where(timer => timer.ArmedAt > mark)
                .Select(timer => timer.DueAt!.Value - now)
                .Concat(_deadlines.Where(deadline => deadline.ArmedAt > mark).Select(deadline => deadline.At - now))
                .Order()
                .ToList();
        }
    }

    public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
    {
        Argument.IsNotNull(callback);

        var timer = new SimulatedTimer(this, callback, state);
        timer.Change(dueTime, period);

        return timer;
    }

    private Task _Register(DateTimeOffset at, CancellationToken cancellationToken)
    {
        var deadline = new Deadline(at, Interlocked.Increment(ref _armSequence));
        _deadlines.Add(deadline);

        if (cancellationToken.CanBeCanceled)
        {
            deadline.Cancellation = cancellationToken.Register(
                static state =>
                {
                    var (clock, registered, token) = ((SimulationClock, Deadline, CancellationToken))state!;
                    lock (clock._gate)
                    {
                        clock._deadlines.Remove(registered);
                    }

                    registered.Reached.TrySetCanceled(token);
                },
                (this, deadline, cancellationToken)
            );
        }

        return deadline.Reached.Task;
    }

    /// <summary>Arms or disarms <paramref name="timer"/>; false once it is disposed, as <see cref="ITimer.Change"/> requires.</summary>
    private bool _Arm(SimulatedTimer timer, TimeSpan dueTime, TimeSpan period)
    {
        bool due;
        lock (_gate)
        {
            if (timer.Disposed)
            {
                return false;
            }

            _timers.Remove(timer);

            // A negative due time other than infinite is as invalid here as on the system clock's timers; both disarm.
            if (dueTime < TimeSpan.Zero)
            {
                timer.DueAt = null;
                return true;
            }

            timer.ArmedAt = Interlocked.Increment(ref _armSequence);
            timer.Period = period > TimeSpan.Zero ? period : TimeSpan.Zero;
            timer.DueAt = _now + dueTime;
            _timers.Add(timer);
            due = dueTime == TimeSpan.Zero;
        }

        // A zero due time fires inside the call that armed it, as on a fake clock.
        if (due)
        {
            _FireDueTimers();
        }

        return true;
    }

    private void _Disarm(SimulatedTimer timer)
    {
        lock (_gate)
        {
            timer.Disposed = true;
            timer.DueAt = null;
            _timers.Remove(timer);
        }
    }

    private void _FireDueTimers()
    {
        if (Interlocked.CompareExchange(ref _firing, 1, 0) != 0)
        {
            // Another thread is firing; it scans again before it stops, so it fires these too.
            return;
        }

        while (true)
        {
            SimulatedTimer? next = null;
            lock (_gate)
            {
                foreach (var timer in _timers)
                {
                    if (
                        timer.DueAt <= _now
                        && (
                            next is null
                            || timer.DueAt < next.DueAt
                            || (timer.DueAt == next.DueAt && timer.ArmedAt < next.ArmedAt)
                        )
                    )
                    {
                        next = timer;
                    }
                }

                if (next is null)
                {
                    // Released under the gate: an arm that adds a due timer after this scan then fires it itself.
                    _firing = 0;
                    return;
                }

                // Rescheduled before the callback runs, so a callback that re-arms or disposes its own timer stands; a
                // periodic timer the clock jumped past fires once per period it missed, as on a fake clock.
                if (next.Period > TimeSpan.Zero)
                {
                    next.DueAt += next.Period;
                }
                else
                {
                    next.DueAt = null;
                    _timers.Remove(next);
                }
            }

            try
            {
                next.Callback(next.State);
            }
            catch
            {
                // A callback that throws would otherwise leave the clock firing on nobody's thread, so no later advance
                // could fire anything.
                Volatile.Write(ref _firing, 0);
                throw;
            }
        }
    }

    private sealed class Deadline(DateTimeOffset at, long armedAt)
    {
        public DateTimeOffset At { get; } = at;

        public long ArmedAt { get; } = armedAt;

        public TaskCompletionSource Reached { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public CancellationTokenRegistration Cancellation { get; set; }
    }

    /// <summary>A timer on the simulation clock; it fires only when an advance passes its due time.</summary>
    private sealed class SimulatedTimer(SimulationClock clock, TimerCallback callback, object? state) : ITimer
    {
        public TimerCallback Callback { get; } = callback;

        public object? State { get; } = state;

        // Every property below is guarded by the clock's gate. A timer is in the clock's list exactly while DueAt is set.
        public long ArmedAt { get; set; }

        public DateTimeOffset? DueAt { get; set; }

        public TimeSpan Period { get; set; }

        public bool Disposed { get; set; }

        public bool Change(TimeSpan dueTime, TimeSpan period) => clock._Arm(this, dueTime, period);

        public void Dispose() => clock._Disarm(this);

        public ValueTask DisposeAsync()
        {
            Dispose();
            return ValueTask.CompletedTask;
        }
    }
}
