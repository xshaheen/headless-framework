// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Microsoft.Extensions.Time.Testing;

namespace Tests.Fakes;

/// <summary>
/// A <see cref="FakeTimeProvider"/> that also records every timer callback it creates, so a test can run a callback by
/// hand after the timer was rearmed. That reproduces a callback the runtime had already queued when another thread
/// moved the timer, which <see cref="FakeTimeProvider.Advance"/> alone cannot produce.
/// </summary>
internal sealed class CallbackCapturingTimeProvider : FakeTimeProvider
{
    private readonly List<(TimerCallback Callback, object? State)> _timers = [];

    public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
    {
        lock (_timers)
        {
            _timers.Add((callback, state));
        }

        return base.CreateTimer(callback, state, dueTime, period);
    }

    /// <summary>Runs the callback of every recorded timer whose state is an instance of the type named <paramref name="stateTypeName"/>.</summary>
    public void RunCallbacks(string stateTypeName)
    {
        (TimerCallback Callback, object? State)[] matches;

        lock (_timers)
        {
            matches =
            [
                .. _timers.Where(timer =>
                    string.Equals(timer.State?.GetType().Name, stateTypeName, StringComparison.Ordinal)
                ),
            ];
        }

        matches.Should().NotBeEmpty("pre-condition: a timer with that state type was created");

        foreach (var (callback, state) in matches)
        {
            callback(state);
        }
    }
}
