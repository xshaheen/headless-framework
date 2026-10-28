// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Coordination;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Headless.Jobs.Coordination;

/// <summary>
/// Jobs reclaim sink for the shared <see cref="DeadOwnerRecoveryBridge{TReclaimer}"/>. Releases the
/// operational-store resources owned by a dead node identity; the skip-in-flight policy lives inside
/// <see cref="IInternalJobManager.ReleaseDeadNodeResources(IReadOnlyCollection{string}, CancellationToken)"/>.
/// </summary>
internal sealed class JobsDeadOwnerReclaimer(
    IInternalJobManager internalJobManager,
    SchedulerOptionsBuilder optionsBuilder,
    IOptions<CoordinationOptions>? coordinationOptions = null,
    ILogger<JobsDeadOwnerReclaimer>? logger = null
) : IDeadOwnerReclaimer
{
    private readonly IInternalJobManager _internalJobManager = internalJobManager;

    /// <summary>
    /// The effective reconcile cadence, shared with the fallback service's orphaned-owner sweep so both run on one
    /// value. Resolved and, when clamped, logged once per host.
    /// </summary>
    public TimeSpan ReconcileInterval { get; } =
        DeadOwnerReconcileInterval.Resolve(
            optionsBuilder.DeadNodeReconcileInterval,
            coordinationOptions?.Value,
            logger ?? NullLogger<JobsDeadOwnerReclaimer>.Instance
        );

    public async Task ReclaimAsync(IReadOnlyCollection<string> owners, CancellationToken cancellationToken)
    {
        // IDeadOwnerReclaimer contract: a reclaim racing host shutdown must complete, so the durable batch
        // uses CancellationToken.None and does not re-thread the incoming token (matches MessagingDeadOwnerReclaimer).
        //
        // Intentionally NOT distributed-lock guarded (#267 review): the bridge marks each owner reclaimed *before*
        // calling us and only un-marks on a thrown exception, so a skip-on-contention that returned normally would
        // pin the owner and strand its dead-node Idle/Queued rows — this sweep is their only exact-owner release
        // path (the stalled-lease sweep independently recovers InProgress rows once their lease lapses). Exact-owner
        // predicates already make a repeated sweep touch zero rows, so a coarse lock would only remove redundant
        // concurrent sweeps on rare node deaths — never worth making this the correctness boundary it must not be.
        //
        // The manager continues past per-owner failures, aggregates them for bridge retry, and performs the global
        // terminal-child reconciliation once for the whole disruption rather than once per dead owner.
        await _internalJobManager.ReleaseDeadNodeResources(owners, CancellationToken.None).ConfigureAwait(false);
    }
}
