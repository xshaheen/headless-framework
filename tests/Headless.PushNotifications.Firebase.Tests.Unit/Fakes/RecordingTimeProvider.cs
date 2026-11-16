// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Microsoft.Extensions.Time.Testing;

namespace Tests.Fakes;

/// <summary>
/// A <see cref="FakeTimeProvider"/> that records every finite timer it creates. A retry delay is a timer, so a test
/// can wait until the sender starts waiting, read exactly how long it asked to wait, and only then advance the clock.
/// Advancing before the timer exists would skip past it.
/// </summary>
internal sealed class RecordingTimeProvider()
    : FakeTimeProvider(new DateTimeOffset(2026, 9, 27, 12, 0, 0, TimeSpan.Zero))
{
    private readonly Lock _sync = new();
    private readonly List<TimeSpan> _timers = [];
    private readonly List<(int Count, TaskCompletionSource Source)> _waiters = [];

    public IReadOnlyList<TimeSpan> Timers
    {
        get
        {
            lock (_sync)
            {
                return [.. _timers];
            }
        }
    }

    public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
    {
        var timer = base.CreateTimer(callback, state, dueTime, period);

        if (dueTime != Timeout.InfiniteTimeSpan)
        {
            lock (_sync)
            {
                _timers.Add(dueTime);

                for (var i = _waiters.Count - 1; i >= 0; i--)
                {
                    if (_timers.Count >= _waiters[i].Count)
                    {
                        _waiters[i].Source.TrySetResult();
                        _waiters.RemoveAt(i);
                    }
                }
            }
        }

        return timer;
    }

    /// <summary>Waits, bounded, for the <paramref name="count"/>th timer and returns its due time.</summary>
    public async Task<TimeSpan> WaitForTimerAsync(int count, CancellationToken cancellationToken)
    {
        Task created;

        lock (_sync)
        {
            if (_timers.Count >= count)
            {
                return _timers[count - 1];
            }

            var source = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            _waiters.Add((count, source));
            created = source.Task;
        }

        await created.WaitAsync(cancellationToken).Bounded();

        return Timers[count - 1];
    }
}
