// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Messaging.Internal;
using Headless.Testing.Tests;

namespace Tests;

public sealed class ScheduledMediumMessageQueueTests : TestBase
{
    [Fact]
    public void should_allow_finalization_when_not_explicitly_disposed()
    {
        // given
        var weakReference = _CreateUndisposedQueue();

        // when
        for (var i = 0; i < 8 && weakReference.IsAlive; i++)
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
        }

        // then
        weakReference.IsAlive.Should().BeFalse();
    }

    private static WeakReference _CreateUndisposedQueue()
    {
        var queue = new ScheduledMediumMessageQueue(TimeProvider.System);
        return new WeakReference(queue, trackResurrection: false);
    }
}
