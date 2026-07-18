// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Tests.Fakes;

/// <summary>Scheduling helpers for composite-acquisition tests. Shared by the mutex, reader-writer, and semaphore suites.</summary>
internal static class CompositeTestScheduler
{
    /// <summary>Bounds the drain so a condition that never holds fails the assertion instead of hanging the run.</summary>
    private static readonly TimeSpan _DrainTimeout = TimeSpan.FromSeconds(10);

    /// <summary>
    /// Yields until <paramref name="condition"/> holds, then asserts it. The coordinator's formation-renewal loop runs
    /// as detached continuations, so advancing a <c>FakeTimeProvider</c> only queues its next step — the test still has
    /// to let the scheduler drain before the effect is observable.
    /// </summary>
    /// <remarks>
    /// The bound is wall-clock, not a fixed yield count. A yield budget measures scheduler turns rather than progress,
    /// so on a loaded machine — a CI agent, or a run that starts while a build is still finishing — the continuation can
    /// simply not have been dispatched yet, and the drain gives up on a condition that was about to hold. That failure
    /// is indistinguishable from a real regression while depending on nothing but machine load, so it is worth spending
    /// idle time to rule out.
    /// </remarks>
    internal static async Task DrainUntilAsync(Func<bool> condition)
    {
        var start = TimeProvider.System.GetTimestamp();

        while (!condition() && TimeProvider.System.GetElapsedTime(start) < _DrainTimeout)
        {
            await Task.Yield();
        }

        condition().Should().BeTrue();
    }
}
