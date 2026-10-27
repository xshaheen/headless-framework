// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Coordination;
using Headless.Jobs.BackgroundServices;

namespace Headless.Jobs.Coordination;

/// <summary>
/// Holds this node in coordination membership until the Jobs scheduler has drained running jobs and written their
/// outcomes, so no peer reclaims a row this node is still settling.
/// </summary>
/// <remarks>
/// It completes when the scheduler's own stop has drained. The heartbeat asks only after every hosted service has
/// stopped, so the wait never blocks the scheduler's stop, whichever of the two is registered first.
/// </remarks>
internal sealed class JobsDrainParticipant(JobsSchedulerBackgroundService scheduler) : INodeDrainParticipant
{
    public Task WaitUntilDrainedAsync(CancellationToken cancellationToken)
    {
        return scheduler.WaitUntilDrainedAsync(cancellationToken);
    }
}
