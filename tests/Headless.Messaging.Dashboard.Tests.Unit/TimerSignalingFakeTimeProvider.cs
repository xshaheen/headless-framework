// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Microsoft.Extensions.Time.Testing;

namespace Tests;

/// <summary>
/// A fake clock that reports the timers the product arms, so a test moves time only after the timer it means to fire
/// exists.
/// </summary>
/// <remarks>
/// A poller's <see cref="PeriodicTimer"/> is created once and keeps ticking, so an advance that lands before it exists is
/// lost and one that lands after it queues a tick whether or not the poller is waiting yet. Watching
/// <see cref="CreateTimer"/> tells the test which of the two it is in, whichever code path arms the timer.
/// </remarks>
internal sealed class TimerSignalingFakeTimeProvider(DateTimeOffset startDateTime) : FakeTimeProvider(startDateTime)
{
    private readonly Lock _lock = new();
    private readonly Dictionary<TimeSpan, TaskCompletionSource> _armed = [];

    /// <summary>
    /// Returns a task that completes once a timer due in <paramref name="dueTime"/> exists: at once if one was created
    /// already, otherwise when it is. A periodic timer lives on after its creation, so this is a state, not an event.
    /// </summary>
    public Task WhenTimerArmed(TimeSpan dueTime)
    {
        lock (_lock)
        {
            return _Signal(dueTime).Task;
        }
    }

    public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
    {
        // Signaled only after the clock has scheduled the timer, so an advance the signal triggers fires it.
        var timer = base.CreateTimer(callback, state, dueTime, period);

        lock (_lock)
        {
            _Signal(dueTime).TrySetResult();
        }

        return timer;
    }

    private TaskCompletionSource _Signal(TimeSpan dueTime)
    {
        if (!_armed.TryGetValue(dueTime, out var signal))
        {
            signal = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            _armed[dueTime] = signal;
        }

        return signal;
    }
}
