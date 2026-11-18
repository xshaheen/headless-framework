// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Threading.Channels;
using Microsoft.Extensions.Time.Testing;

namespace Tests;

/// <summary>
/// A fake clock that reports each timer the code under test arms, so a test advances it only once the code is parked
/// on its delay. A timer created after an advance is measured from the later time and fires nothing.
/// </summary>
internal sealed class TimerSignalingTimeProvider : FakeTimeProvider
{
    private readonly Channel<TimeSpan> _createdTimers = Channel.CreateUnbounded<TimeSpan>();

    public TimerSignalingTimeProvider() { }

    public TimerSignalingTimeProvider(DateTimeOffset startDateTime)
        : base(startDateTime) { }

    public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
    {
        var timer = base.CreateTimer(callback, state, dueTime, period);
        _createdTimers.Writer.TryWrite(dueTime);

        return timer;
    }

    /// <summary>Waits for the next timer armed that no earlier call consumed, and returns its due time.</summary>
    public Task<TimeSpan> NextTimerAsync(CancellationToken cancellationToken)
    {
        return _createdTimers.Reader.ReadAsync(cancellationToken).AsTask();
    }

    /// <summary>Waits until a timer with <paramref name="dueTime"/> is armed, skipping the others.</summary>
    public async Task WaitForTimerAsync(TimeSpan dueTime, CancellationToken cancellationToken)
    {
        while (await NextTimerAsync(cancellationToken) != dueTime) { }
    }
}
