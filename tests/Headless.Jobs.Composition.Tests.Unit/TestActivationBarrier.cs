// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Jobs.Internal;

namespace Tests;

internal static class TestActivationBarrier
{
    /// <summary>
    /// An activation barrier that is already open. Tests driving a background loop directly (without
    /// <c>JobsInitializationHostedService</c>) need this, otherwise the loop parks on its startup gate forever.
    /// </summary>
    public static JobsActivationBarrier Opened()
    {
        var barrier = new JobsActivationBarrier();
        barrier.MarkCompleted();

        return barrier;
    }
}
