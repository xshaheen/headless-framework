// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Microsoft.Extensions.Time.Testing;

namespace Tests;

/// <summary>A clock a test advances, which also tells the test when something started to wait on it.</summary>
internal sealed class RerunClock : FakeTimeProvider
{
    private readonly TaskCompletionSource _waiting = new(TaskCreationOptions.RunContinuationsAsynchronously);

    /// <summary>Completes once a wait on this clock has begun.</summary>
    public Task Waiting => _waiting.Task;

    public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
    {
        var timer = base.CreateTimer(callback, state, dueTime, period);
        _waiting.TrySetResult();

        return timer;
    }
}
