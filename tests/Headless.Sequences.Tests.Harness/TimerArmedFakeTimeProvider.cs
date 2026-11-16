// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Threading.Channels;
using Microsoft.Extensions.Time.Testing;

namespace Tests;

/// <summary>
/// A fake clock that reports every timer it arms, so a test moves it only once the code under test is waiting on it.
/// </summary>
/// <remarks>
/// A retry arms its delay timer only after the failed attempt has finished its work against the store. Advancing the
/// clock before that point is lost, and stepping it on a real-time poll only guesses when the timer exists. Waiting
/// for the arm signal orders the advance after the timer without a real-time wait, and proves the code is parked on
/// the clock rather than retrying on its own.
/// </remarks>
public sealed class TimerArmedFakeTimeProvider : FakeTimeProvider
{
    private readonly Channel<TimeSpan> _armed = Channel.CreateUnbounded<TimeSpan>();

    public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
    {
        var timer = base.CreateTimer(callback, state, dueTime, period);
        _armed.Writer.TryWrite(dueTime);

        return timer;
    }

    /// <summary>Waits for the next timer this clock arms, or one armed since the last wait, and returns its due time.</summary>
    public Task<TimeSpan> WaitForArmedTimerAsync(CancellationToken cancellationToken)
    {
        return _armed.Reader.ReadAsync(cancellationToken).AsTask().Bounded();
    }

    /// <summary>
    /// Advances the clock by each timer's own due time as soon as it is armed, until <paramref name="pending" /> ends,
    /// so a failed <paramref name="pending" /> ends the wait at once instead of after a timeout.
    /// </summary>
    public async Task AdvanceWhenArmedAsync(Task pending, CancellationToken cancellationToken)
    {
        while (!pending.IsCompleted)
        {
            var armed = _armed.Reader.ReadAsync(cancellationToken).AsTask();

            if (await Task.WhenAny(armed, pending).Bounded() == pending)
            {
                return;
            }

            Advance(await armed);
        }
    }
}
