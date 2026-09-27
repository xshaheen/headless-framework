// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Collections.Concurrent;
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
    private readonly ConcurrentQueue<TimeSpan> _timers = new();

    public IReadOnlyList<TimeSpan> Timers => [.. _timers];

    public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
    {
        var timer = base.CreateTimer(callback, state, dueTime, period);

        if (dueTime != Timeout.InfiniteTimeSpan)
        {
            _timers.Enqueue(dueTime);
        }

        return timer;
    }

    /// <summary>Waits in real time, bounded, for the <paramref name="count"/>th timer and returns its due time.</summary>
    public async Task<TimeSpan> WaitForTimerAsync(int count, CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(30));

        while (_timers.Count < count)
        {
            await Task.Delay(TimeSpan.FromMilliseconds(5), timeout.Token);
        }

        return Timers[count - 1];
    }
}
