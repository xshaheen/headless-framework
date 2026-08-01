// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Coordination;
using Headless.Jobs.Interfaces.Managers;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Headless.Jobs.Coordination;

/// <summary>
/// Jobs reclaim sink for the shared <see cref="DeadOwnerRecoveryBridge{TReclaimer}"/>. Releases the
/// operational-store resources owned by a dead node identity; the skip-in-flight policy lives inside
/// <see cref="IInternalJobManager.ReleaseDeadNodeResources"/>.
/// </summary>
internal sealed class JobsDeadOwnerReclaimer(
    IInternalJobManager internalJobManager,
    SchedulerOptionsBuilder optionsBuilder,
    IOptions<CoordinationOptions>? coordinationOptions = null,
    ILogger<JobsDeadOwnerReclaimer>? logger = null
) : IDeadOwnerReclaimer
{
    private readonly IInternalJobManager _internalJobManager = internalJobManager;

    public TimeSpan ReconcileInterval { get; } =
        _ClampToDeadVisibilityWindow(
            optionsBuilder.DeadNodeReconcileInterval,
            coordinationOptions?.Value,
            logger ?? NullLogger<JobsDeadOwnerReclaimer>.Instance
        );

    public async Task ReclaimAsync(IReadOnlyCollection<string> owners, CancellationToken cancellationToken)
    {
        // Jobs releases per-owner resources, so a batch is a loop. KTD6 / IDeadOwnerReclaimer contract: a reclaim
        // racing host shutdown must complete, so each write uses CancellationToken.None and does not re-thread the
        // incoming token (matches MessagingDeadOwnerReclaimer). The bridge already hands us None; this hardens it.
        //
        // Intentionally NOT distributed-lock guarded (#267 review): the bridge marks each owner reclaimed *before*
        // calling us and only un-marks on a thrown exception, so a skip-on-contention that returned normally would
        // pin the owner and strand its dead-node Idle/Queued rows — this sweep is their only exact-owner release
        // path (the stalled-lease sweep independently recovers InProgress rows once their lease lapses). Exact-owner
        // predicates already make a repeated sweep touch zero rows, so a coarse lock would only remove redundant
        // concurrent sweeps on rare node deaths — never worth making this the correctness boundary it must not be.
        //
        // Continue-on-error: one owner's transient failure must not skip the rest of the batch — on the relational
        // providers the Dead state is visible for only tens of seconds, so an owner skipped this tick can age out of
        // the snapshot before the next one. Failures aggregate and propagate so the bridge's retry stays intact.
        List<Exception>? failures = null;

        foreach (var owner in owners)
        {
            try
            {
                await _internalJobManager.ReleaseDeadNodeResources(owner, CancellationToken.None).ConfigureAwait(false);
            }
            catch (Exception exception)
            {
                (failures ??= []).Add(exception);
            }
        }

        if (failures is not null)
        {
            throw new AggregateException(
                "One or more dead-owner reclaims failed; the bridge will retry them.",
                failures
            );
        }
    }

    // Relational membership providers prune a dead identity DeadRetentionWindow after DeadThreshold, so Dead is
    // observable for only that window. The reconcile is documented as the authoritative backstop when a NodeLeft
    // event is missed — with a reconcile interval wider than half the visibility window, a dead owner has roughly
    // coin-flip odds of ever being observed Dead. Clamp rather than validate: the two values live in different
    // options classes (Jobs vs Coordination) and neither owns the other.
    private static TimeSpan _ClampToDeadVisibilityWindow(
        TimeSpan configured,
        CoordinationOptions? coordination,
        ILogger logger
    )
    {
        if (coordination is null)
        {
            return configured;
        }

        var ceiling = TimeSpan.FromTicks(coordination.DeadRetentionWindow.Ticks / 2);
        if (ceiling <= TimeSpan.Zero || configured <= ceiling)
        {
            return configured;
        }

        logger.LogJobsReconcileIntervalClamped(configured, ceiling, coordination.DeadRetentionWindow);
        return ceiling;
    }
}

internal static partial class JobsDeadOwnerReclaimerLog
{
    [LoggerMessage(
        EventId = 3230,
        Level = LogLevel.Warning,
        Message = "DeadNodeReconcileInterval {Configured} exceeds half the coordination Dead-visibility window "
            + "(DeadRetentionWindow {DeadRetentionWindow}); clamped to {Effective} so a dead owner whose NodeLeft "
            + "event was missed is still observed before its snapshot entry is pruned."
    )]
    public static partial void LogJobsReconcileIntervalClamped(
        this ILogger logger,
        TimeSpan configured,
        TimeSpan effective,
        TimeSpan deadRetentionWindow
    );
}
