// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Tests;

/// <summary>
/// Wraps a fake clock and reports when the cache arms a timer, so a test advances time only after the timer it
/// means to fire exists.
/// </summary>
/// <remarks>
/// A cache arms its factory-timeout and lock-wait timers on its own schedule, after the call has started. Advancing
/// a fake clock before that point leaves the timer due at the advanced "now" plus its timeout, with nothing left to
/// move the clock. Watching <see cref="TimeProvider.CreateTimer"/> catches every timer the cache creates through its
/// clock, whichever code path arms it, without reaching into the cache's internals.
/// </remarks>
public sealed class TimerSignalingTimeProvider(TimeProvider inner) : TimeProvider
{
    private readonly Lock _lock = new();
    private readonly List<(TimeSpan DueTime, TaskCompletionSource Armed)> _watchers = [];

    public override TimeZoneInfo LocalTimeZone => inner.LocalTimeZone;

    public override long TimestampFrequency => inner.TimestampFrequency;

    public override DateTimeOffset GetUtcNow() => inner.GetUtcNow();

    public override long GetTimestamp() => inner.GetTimestamp();

    /// <summary>
    /// Returns a task that completes when the next timer whose due time is <paramref name="dueTime"/> is created.
    /// Call it before starting the operation that arms the timer, so a timer armed earlier by another call never
    /// matches.
    /// </summary>
    public Task WatchForTimer(TimeSpan dueTime)
    {
        var armed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        lock (_lock)
        {
            _watchers.Add((dueTime, armed));
        }

        return armed.Task;
    }

    public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
    {
        // Signal only after the inner clock has scheduled the timer, so an advance the watcher triggers fires it.
        var timer = inner.CreateTimer(callback, state, dueTime, period);

        lock (_lock)
        {
            for (var i = _watchers.Count - 1; i >= 0; i--)
            {
                if (_watchers[i].DueTime == dueTime)
                {
                    _watchers[i].Armed.TrySetResult();
                    _watchers.RemoveAt(i);
                }
            }
        }

        return timer;
    }
}
