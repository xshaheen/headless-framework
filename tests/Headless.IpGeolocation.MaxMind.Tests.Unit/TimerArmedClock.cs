// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Microsoft.Extensions.Time.Testing;

namespace Tests;

/// <summary>
/// A <see cref="FakeTimeProvider"/> that reports when code creates a timer with a given due time, so a test can wait
/// until a background loop is parked on its delay before it advances time. An advance that lands before the timer
/// exists is lost, because the timer is created against the later time.
/// </summary>
public sealed class TimerArmedClock(DateTimeOffset start) : FakeTimeProvider(start)
{
    private readonly Lock _sync = new();
    private readonly List<TimeSpan> _dueTimes = [];
    private readonly List<(TimeSpan DueTime, TaskCompletionSource Source)> _waiters = [];

    public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
    {
        var timer = base.CreateTimer(callback, state, dueTime, period);

        lock (_sync)
        {
            _dueTimes.Add(dueTime);

            for (var i = _waiters.Count - 1; i >= 0; i--)
            {
                if (_waiters[i].DueTime == dueTime)
                {
                    _waiters[i].Source.TrySetResult();
                    _waiters.RemoveAt(i);
                }
            }
        }

        return timer;
    }

    /// <summary>Completes once a timer with <paramref name="dueTime"/> has been created, now or later.</summary>
    public Task WhenTimerArmedAsync(TimeSpan dueTime)
    {
        lock (_sync)
        {
            if (_dueTimes.Contains(dueTime))
            {
                return Task.CompletedTask;
            }

            var source = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            _waiters.Add((dueTime, source));

            return source.Task;
        }
    }
}
