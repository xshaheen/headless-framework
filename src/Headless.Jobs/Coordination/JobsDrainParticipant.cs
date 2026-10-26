// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Coordination;
using Headless.Jobs.BackgroundServices;

namespace Headless.Jobs.Coordination;

/// <summary>
/// Holds this node in coordination membership until the Jobs scheduler has drained running jobs and written their
/// outcomes, so no peer reclaims a row this node is still settling.
/// </summary>
/// <remarks>
/// Under the default sequential host stop the scheduler, registered after coordination, stops first and this
/// completes at once. It holds the node when <c>HostOptions.ServicesStopConcurrently</c> stops both together.
/// </remarks>
internal sealed class JobsDrainParticipant(JobsSchedulerBackgroundService scheduler) : INodeDrainParticipant
{
    public Task WaitUntilDrainedAsync(CancellationToken cancellationToken)
    {
        return scheduler.WaitUntilDrainedAsync(cancellationToken);
    }
}
