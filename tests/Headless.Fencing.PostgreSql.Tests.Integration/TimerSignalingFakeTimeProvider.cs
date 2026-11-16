// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Microsoft.Extensions.Time.Testing;

namespace Tests;

/// <summary>
/// A fake clock that reports the timers the product arms, so a test moves time only after the delay it means to fire
/// exists: an advance that lands before the retry has armed its delay is lost.
/// </summary>
internal sealed class TimerSignalingFakeTimeProvider : FakeTimeProvider
{
    private readonly Lock _lock = new();
    private readonly List<(int Count, TaskCompletionSource Signal)> _waiters = [];
    private int _armed;

    /// <summary>Completes once at least <paramref name="count" /> timers have been created on this clock.</summary>
    public Task WhenTimersArmed(int count)
    {
        lock (_lock)
        {
            if (_armed >= count)
            {
                return Task.CompletedTask;
            }

            var signal = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            _waiters.Add((count, signal));

            return signal.Task;
        }
    }

    public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
    {
        // Signaled only after the clock has scheduled the timer, so an advance the signal triggers fires it.
        var timer = base.CreateTimer(callback, state, dueTime, period);

        lock (_lock)
        {
            _armed++;

            foreach (var (count, signal) in _waiters.Where(w => w.Count <= _armed).ToList())
            {
                signal.TrySetResult();
                _waiters.Remove((count, signal));
            }
        }

        return timer;
    }
}
