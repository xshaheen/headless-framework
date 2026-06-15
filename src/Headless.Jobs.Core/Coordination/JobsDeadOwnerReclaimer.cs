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
        // KTD6 / IDeadOwnerReclaimer contract: a reclaim racing host shutdown must complete, so the write
        // deliberately uses CancellationToken.None and does not re-thread the incoming token (matches
        // MessagingDeadOwnerReclaimer). The bridge already hands us None; this hardens the contract.
        internalJobManager.ReleaseDeadNodeResources(owner, CancellationToken.None);
}
