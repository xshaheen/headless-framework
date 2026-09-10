// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;
using Headless.Jobs.Enums;
using Headless.Jobs.Models;

namespace Headless.Jobs;

/// <summary>
/// The coalesce-recovery DECISION, storage-agnostic and pure: which instant to materialize at, which existing row to
/// repurpose, which to step past, which to retire, and where the resolution window ends under a saturated evaluation.
/// Every provider plans with this and then applies the returned plan as fenced writes of its own.
/// </summary>
/// <remarks>
/// <para>
/// Before this existed the decision was hand-mirrored in the relational and in-memory providers with matching rule-ID
/// comments and no shared code, covered on one side only by the EF harness and on the other only by unit tests. CI
/// runs the unit suite alone, so a divergence surfaced as a comment mismatch rather than a failing test.
/// </para>
/// <para>
/// The planner does NOT restate the occupied-instant rule. It calls
/// <see cref="CronOccurrenceAccounting.IsInstantAccountedFor" /> and
/// <see cref="CronOccurrenceAccounting.LiveFirstRank" /> over rows the providers project through the SAME selector
/// that <c>MaterializeCronScheduleOccurrenceAsync</c> uses, so materialization and recovery still resolve one row
/// identically — a property that was measured broken before it was single-sourced (KTD1c).
/// </para>
/// </remarks>
[PublicAPI]
public static class CronRecoveryPlanner
{
    /// <summary>
    /// The span of occurrence rows a provider must snapshot before planning. Read it with the provider's own
    /// mechanics — an unlocked query inside the recovery transaction, or the values held under the in-memory
    /// per-definition lock — and hand the projected rows to <see cref="CreatePlan" />.
    /// </summary>
    /// <param name="request">The recovery request being planned.</param>
    /// <exception cref="ArgumentNullException"><paramref name="request" /> is <see langword="null" />.</exception>
    public static CronRecoveryWindow GetInspectionWindow(CronRecoveryRequest request)
    {
        Argument.IsNotNull(request);

        return new CronRecoveryWindow
        {
            StartExclusiveUtc = request.ObservedReconciledThroughUtc,
            EndInclusiveUtc = _IsBoundedInspection(request)
                ? request.BoundedProgressThroughUtc
                : request.RecoveredThroughUtc,
        };
    }

    /// <summary>
    /// Resolves the whole recovery decision against a snapshot of the window. Pure: it reads nothing, writes nothing,
    /// and calls back into no storage.
    /// </summary>
    /// <param name="request">The recovery request being planned.</param>
    /// <param name="rowsInWindow">
    /// Every occurrence row inside <see cref="GetInspectionWindow" />, projected through
    /// <see cref="CronOccurrenceAccounting.InstantViewSelector{TCronJob}" /> (or its compiled twin). Rows outside the
    /// window are ignored, so passing a wider snapshot is safe but pointless.
    /// </param>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="request" /> or <paramref name="rowsInWindow" /> is <see langword="null" />.
    /// </exception>
    public static CronRecoveryPlan CreatePlan(
        CronRecoveryRequest request,
        IReadOnlyCollection<CronOccurrenceInstantView> rowsInWindow
    )
    {
        Argument.IsNotNull(request);
        Argument.IsNotNull(rowsInWindow);

        // Only a saturated coalesce pass that establishes NO run is confined to its examined prefix. Having found the
        // one run the backlog owes, it stands for the whole store-time window and resolves all of it; under Skip, or
        // unsaturated, there is no prefix to be confined to.
        var confinedWhenEmptyHanded = _IsBoundedInspection(request);

        return new CronRecoveryPlan
        {
            RunSteps = _PlanRunSteps(request, rowsInWindow),
            WhenRunEstablished = new CronRecoveryResolution
            {
                RetireFromExclusiveUtc = request.ObservedReconciledThroughUtc,
                RetireThroughInclusiveUtc = request.RecoveredThroughUtc,
                ReconciledThroughUtc = request.RecoveredThroughUtc,
                NextDueUtc = request.NextDueUtc,
            },
            WhenNoRunEstablished = new CronRecoveryResolution
            {
                RetireFromExclusiveUtc = request.ObservedReconciledThroughUtc,
                RetireThroughInclusiveUtc = confinedWhenEmptyHanded
                    ? request.BoundedProgressThroughUtc
                    : request.RecoveredThroughUtc,
                ReconciledThroughUtc = confinedWhenEmptyHanded
                    ? request.BoundedProgressThroughUtc
                    : request.RecoveredThroughUtc,
                NextDueUtc = confinedWhenEmptyHanded ? request.NextDueAfterBoundedProgressUtc : request.NextDueUtc,
            },
        };
    }

    private static bool _IsBoundedInspection(CronRecoveryRequest request)
    {
        return request.Policy is MissedRunPolicy.Coalesce && request.EvaluationSaturated;
    }

    private static List<CronRecoveryRunStep> _PlanRunSteps(
        CronRecoveryRequest request,
        IReadOnlyCollection<CronOccurrenceInstantView> rowsInWindow
    )
    {
        if (request.Policy is not MissedRunPolicy.Coalesce)
        {
            // Skip deliberately owes nothing: the backlog is retired and no run stands in for it.
            return [];
        }

        // R18 owes the backlog exactly one run; R7 forbids duplicating an instant an executing or terminal row
        // already accounts for. Reconciled by walking the missed instants in schedule order and materializing at the
        // FIRST unaccounted-for one — an occupied instant is stepped past, never duplicated, and only a
        // fully-accounted-for backlog produces no run at all.
        var steps = new List<CronRecoveryRunStep>();

        foreach (var missedInstant in request.MissedInstantsUtc)
        {
            var rowsAtInstant = rowsInWindow.Where(x => x.ExecutionTime == missedInstant).ToArray();

            // A live row takes precedence over a terminal one sharing the instant: the filtered unique index
            // constrains only live rows, so both can stand there, and CreatedAt order alone would surface the older
            // terminal one and repurpose nothing.
            var candidate = rowsAtInstant
                .OrderBy(CronOccurrenceAccounting.LiveFirstRank)
                .ThenBy(x => x.CreatedAt)
                .ThenBy(x => x.Id)
                .FirstOrDefault();

            // KTD1: an instant whose rows NONE account for — a seeding migration retired the only one without a
            // replacement — still owes its run, so it is materialized exactly as an empty instant is. Testing
            // accounting rather than mere presence is what keeps recovery and the claim path from disagreeing about
            // the same row. The null test is implied by the accounting one (an empty instant accounts for nothing)
            // and is stated only so the repurpose branch below sees a non-null candidate.
            if (candidate is null || !CronOccurrenceAccounting.IsInstantAccountedFor(rowsAtInstant))
            {
                // Nothing can make a create fail the way a lost compare-and-set can, so the walk ends here.
                steps.Add(
                    new CronRecoveryRunStep
                    {
                        ExecutionTimeUtc = missedInstant,
                        OccurrenceId = request.CoalescedOccurrenceId,
                        Kind = CronRecoveryRunStepKind.Create,
                    }
                );

                break;
            }

            if (candidate.IsRepurposable)
            {
                // Offered rather than committed: a provider whose snapshot was read without a lock may lose the
                // compare-and-set to a row that started executing, and then continues to the next instant.
                steps.Add(
                    new CronRecoveryRunStep
                    {
                        ExecutionTimeUtc = missedInstant,
                        OccurrenceId = candidate.Id,
                        Kind = CronRecoveryRunStepKind.Repurpose,
                        ExistingCreatedAt = candidate.CreatedAt,
                    }
                );

                continue;
            }

            // Executing or terminal: the instant is accounted for — step past it.
        }

        return steps;
    }
}
