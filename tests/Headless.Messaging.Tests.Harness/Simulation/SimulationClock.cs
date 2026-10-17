// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;
using Microsoft.Extensions.Time.Testing;

namespace Tests.Simulation;

/// <summary>
/// The one clock a simulation runs on. Time moves only when a test calls <see cref="Advance"/>, and the clock keeps a
/// record of every wait armed on it, so a run stuck on simulated time can be told apart from slow work.
/// </summary>
/// <remarks>
/// It wraps a <see cref="FakeTimeProvider"/> rather than deriving from it, so it can see each timer the code under test
/// creates (every <c>Task.Delay</c>, <c>WaitAsync</c>, and <c>PeriodicTimer</c> on a <see cref="TimeProvider"/> goes
/// through <see cref="CreateTimer"/>) and complete its own deadlines atomically with an advance.
/// </remarks>
/// <param name="start">The simulated instant the clock starts at.</param>
[PublicAPI]
public sealed class SimulationClock(DateTimeOffset start) : TimeProvider
{
    private readonly Lock _gate = new();
    private readonly FakeTimeProvider _time = new(start);
    private readonly List<TrackedTimer> _timers = [];
    private readonly List<Deadline> _deadlines = [];
    private long _armSequence;

    /// <summary>Creates a clock that starts at midnight UTC on 1 January 2026.</summary>
    public SimulationClock()
        : this(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero)) { }

    /// <summary>
    /// Gets a mark that moves each time a wait is armed on this clock. <see cref="PendingWaitsArmedSince"/> reads only
    /// the waits armed after a mark, so a caller can ask what a step it started is waiting for.
    /// </summary>
    public long WaitMark => Interlocked.Read(ref _armSequence);

    public override DateTimeOffset GetUtcNow() => _time.GetUtcNow();

    public override long GetTimestamp() => _time.GetTimestamp();

    public override long TimestampFrequency => _time.TimestampFrequency;

    public override TimeZoneInfo LocalTimeZone => TimeZoneInfo.Utc;

    /// <summary>Moves the clock forward, firing every timer and completing every deadline it passes.</summary>
    /// <param name="delta">How far to move; zero fires only what is already due.</param>
    public void Advance(TimeSpan delta)
    {
        Argument.IsPositiveOrZero(delta);

        // The fake clock fires its timers on this thread, and their callbacks run the code under test inline, so the
        // gate is not held across it: a continuation that arms a new wait must not block on this advance.
        _time.Advance(delta);

        List<Deadline> reached;
        lock (_gate)
        {
            var now = _time.GetUtcNow();
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
            if (_time.GetUtcNow() >= deadline)
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
            var deadline = _time.GetUtcNow() + duration;
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
            var now = _time.GetUtcNow();
            return _timers
                .Where(timer => timer.ArmedAt > mark && timer.DueAt is not null)
                .Select(timer => timer.DueAt!.Value - now)
                .Concat(_deadlines.Where(deadline => deadline.ArmedAt > mark).Select(deadline => deadline.At - now))
                .Order()
                .ToList();
        }
    }

    public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
    {
        Argument.IsNotNull(callback);

        var timer = new TrackedTimer(this, callback, state);

        // Armed before the inner timer exists, because a zero due time fires inside CreateTimer itself.
        _Arm(timer, dueTime, period);
        timer.Inner = _time.CreateTimer(static self => ((TrackedTimer)self!).Fire(), timer, dueTime, period);

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

    private void _Arm(TrackedTimer timer, TimeSpan dueTime, TimeSpan period)
    {
        lock (_gate)
        {
            timer.ArmedAt = Interlocked.Increment(ref _armSequence);
            timer.Period = period;
            timer.DueAt = dueTime == Timeout.InfiniteTimeSpan ? null : _time.GetUtcNow() + dueTime;

            if (!timer.Tracked)
            {
                timer.Tracked = true;
                _timers.Add(timer);
            }
        }
    }

    private void _Fired(TrackedTimer timer)
    {
        lock (_gate)
        {
            timer.DueAt =
                timer.Period == Timeout.InfiniteTimeSpan || timer.Period == TimeSpan.Zero
                    ? null
                    : _time.GetUtcNow() + timer.Period;
        }
    }

    private void _Forget(TrackedTimer timer)
    {
        lock (_gate)
        {
            timer.DueAt = null;
            timer.Tracked = false;
            _timers.Remove(timer);
        }
    }

    private sealed class Deadline(DateTimeOffset at, long armedAt)
    {
        public DateTimeOffset At { get; } = at;

        public long ArmedAt { get; } = armedAt;

        public TaskCompletionSource Reached { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public CancellationTokenRegistration Cancellation { get; set; }
    }

    /// <summary>A timer of the fake clock, seen by the simulation clock so its due time can be reported.</summary>
    private sealed class TrackedTimer(SimulationClock clock, TimerCallback callback, object? state) : ITimer
    {
        // Every field below is guarded by the clock's gate.
        public long ArmedAt { get; set; }

        public DateTimeOffset? DueAt { get; set; }

        public TimeSpan Period { get; set; }

        public bool Tracked { get; set; }

        public ITimer? Inner { get; set; }

        public bool Change(TimeSpan dueTime, TimeSpan period)
        {
            clock._Arm(this, dueTime, period);
            return Inner!.Change(dueTime, period);
        }

        public void Fire()
        {
            clock._Fired(this);
            callback(state);
        }

        public void Dispose()
        {
            clock._Forget(this);
            Inner?.Dispose();
        }

        public ValueTask DisposeAsync()
        {
            Dispose();
            return ValueTask.CompletedTask;
        }
    }
}
