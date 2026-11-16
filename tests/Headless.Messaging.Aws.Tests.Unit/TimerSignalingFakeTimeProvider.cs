// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Threading.Channels;
using Microsoft.Extensions.Time.Testing;

namespace Tests;

/// <summary>
/// A fake clock that reports every timer the product arms, so a test moves time only after the timer it means to fire
/// exists.
/// </summary>
/// <remarks>
/// The receive loop, the shutdown drain, and the visibility heartbeat each wait on the clock on their own schedule.
/// An advance that lands before the wait is registered is lost: the timer is then due at the advanced "now" plus its
/// delay, with nothing left to move the clock. Watching <see cref="CreateTimer"/> catches every timer whichever path
/// arms it, without reaching into the client.
/// </remarks>
internal sealed class TimerSignalingFakeTimeProvider : FakeTimeProvider
{
    private readonly Lock _lock = new();
    private readonly List<(TimeSpan DueTime, TaskCompletionSource Armed)> _watchers = [];
    private readonly Channel<bool> _created = Channel.CreateUnbounded<bool>();

    /// <summary>
    /// Returns a task that completes when the next timer due in <paramref name="dueTime"/> is created. Call it before
    /// starting the operation that arms the timer, so a timer armed earlier never matches.
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

    /// <summary>
    /// Advances the clock by <paramref name="step"/> once for each timer the product arms, until
    /// <paramref name="done"/> holds.
    /// </summary>
    /// <remarks>
    /// Every timer is followed by at least one advance made after it was created, so no timer is missed. The caller's
    /// loop must keep arming timers until <paramref name="done"/> holds; a wait that never arrives fails the test
    /// through <c>Bounded</c> instead of hanging it.
    /// </remarks>
    public async Task AdvanceUntilAsync(Func<bool> done, TimeSpan step, CancellationToken cancellationToken)
    {
        while (!done())
        {
            _ = await _created.Reader.ReadAsync(cancellationToken).AsTask().Bounded();
            Advance(step);
        }
    }

    public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
    {
        // Signaled only after the clock has scheduled the timer, so an advance the signal triggers fires it.
        var timer = base.CreateTimer(callback, state, dueTime, period);

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

        _created.Writer.TryWrite(true);
        return timer;
    }
}
