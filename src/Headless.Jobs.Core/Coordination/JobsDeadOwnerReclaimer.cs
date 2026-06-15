// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Coordination;
using Headless.Jobs.Interfaces.Managers;

namespace Headless.Jobs.Coordination;

/// <summary>
/// Jobs reclaim sink for the shared <see cref="DeadOwnerRecoveryBridge{TReclaimer}"/>. Releases the
/// operational-store resources owned by a dead node identity; the skip-in-flight policy lives inside
/// <see cref="IInternalJobManager.ReleaseDeadNodeResources"/>.
/// </summary>
internal sealed class JobsDeadOwnerReclaimer(
    IInternalJobManager internalJobManager,
    SchedulerOptionsBuilder optionsBuilder
) : IDeadOwnerReclaimer
{
    public TimeSpan ReconcileInterval => optionsBuilder.DeadNodeReconcileInterval;

    public Task ReclaimAsync(string owner, CancellationToken cancellationToken) =>
        internalJobManager.ReleaseDeadNodeResources(owner, cancellationToken);
}
