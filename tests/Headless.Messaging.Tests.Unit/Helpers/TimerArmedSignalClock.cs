// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Threading.Channels;
using Microsoft.Extensions.Time.Testing;

namespace Tests.Helpers;

/// <summary>
/// A fake clock that signals each timer it arms, so a test advances it only once a loop has started its next wait.
/// </summary>
/// <remarks>
/// A loop that waits on the clock arms its next timer only after finishing its work, so advancing the clock as soon
/// as that work is seen can land before the timer exists and be lost. Waiting for the arm signal orders the advance
/// after it without a real-time wait.
/// </remarks>
internal sealed class TimerArmedSignalClock(DateTimeOffset start) : FakeTimeProvider(start)
{
    private readonly Channel<bool> _armed = Channel.CreateUnbounded<bool>();

    public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
    {
        var timer = base.CreateTimer(callback, state, dueTime, period);
        _armed.Writer.TryWrite(true);
        return timer;
    }

    /// <summary>Waits for the next timer this clock arms, or one armed since the last call.</summary>
    public Task WaitForArmedTimerAsync()
    {
        return _armed.Reader.ReadAsync(TestContext.Current.CancellationToken).AsTask().Bounded();
    }

    /// <summary>Advances the clock by <paramref name="delta"/> once a timer is armed to observe it.</summary>
    public async Task AdvanceWhenArmedAsync(TimeSpan delta)
    {
        await WaitForArmedTimerAsync();
        Advance(delta);
    }
}
