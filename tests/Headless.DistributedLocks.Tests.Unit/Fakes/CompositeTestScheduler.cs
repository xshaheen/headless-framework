// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Tests.Fakes;

/// <summary>Scheduling helpers for composite-acquisition tests. Shared by the mutex, reader-writer, and semaphore suites.</summary>
internal static class CompositeTestScheduler
{
    /// <summary>
    /// Yields until <paramref name="condition"/> holds, then asserts it. The coordinator's formation-renewal loop runs
    /// as detached continuations, so advancing a <c>FakeTimeProvider</c> only queues its next step — the test still has
    /// to let the scheduler drain before the effect is observable. The attempt cap turns a condition that never holds
    /// into a failed assertion rather than a hung test.
    /// </summary>
    internal static async Task DrainUntilAsync(Func<bool> condition)
    {
        for (var attempt = 0; attempt < 100 && !condition(); attempt++)
        {
            await Task.Yield();
        }

        condition().Should().BeTrue();
    }
}
