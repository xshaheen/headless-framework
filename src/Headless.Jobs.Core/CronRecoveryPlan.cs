// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Jobs;

/// <summary>The half-open span of schedule instants a recovery pass reads before it can decide anything.</summary>
[PublicAPI]
public sealed record CronRecoveryWindow
{
    /// <summary>Exclusive lower bound — the watermark the caller observed.</summary>
    public required DateTime StartExclusiveUtc { get; init; }

    /// <summary>
    /// Inclusive upper bound. Shorter than the recovery instant when a saturated coalesce evaluation examined only a
    /// prefix of the elapsed instants: nothing beyond that prefix has been considered, so nothing beyond it may be
    /// read into the decision.
    /// </summary>
    public required DateTime EndInclusiveUtc { get; init; }
}

/// <summary>Whether the provider creates the coalesced run or repurposes a row that already stands at the instant.</summary>
[PublicAPI]
public enum CronRecoveryRunStepKind
{
    /// <summary>No row accounts for the instant, so the run is materialized under the request's reserved identity.</summary>
    Create = 0,

    /// <summary>A still-claimable row stands at the instant and is revived in place rather than duplicated.</summary>
    Repurpose = 1,
}

/// <summary>
/// One ordered attempt at establishing the coalesced run. The provider walks the steps in order and stops at the
/// first that succeeds.
/// </summary>
/// <remarks>
/// A <see cref="CronRecoveryRunStepKind.Repurpose" /> step can fail: the snapshot the plan was built from is read
/// without a lock in the relational providers, so the row may begin executing before the compare-and-set lands. That
/// is not an error — it means the instant became accounted for, and the provider continues to the next step exactly
/// as the walk would have stepped past an occupied instant. A <see cref="CronRecoveryRunStepKind.Create" /> step
/// cannot fail that way, so it is always the last step in the list.
/// </remarks>
[PublicAPI]
public sealed record CronRecoveryRunStep
{
    /// <summary>The missed instant this step stands at, and the durable recovery stamp the run carries.</summary>
    public required DateTime ExecutionTimeUtc { get; init; }

    /// <summary>Identity of the run: the request's reserved id when creating, the existing row's id when repurposing.</summary>
    public required Guid OccurrenceId { get; init; }

    /// <summary>Which mechanism applies this step.</summary>
    public required CronRecoveryRunStepKind Kind { get; init; }

    /// <summary>
    /// Creation timestamp of the row being repurposed, so the provider can report the run without re-reading it;
    /// <see langword="null" /> for a <see cref="CronRecoveryRunStepKind.Create" /> step.
    /// </summary>
    public DateTimeOffset? ExistingCreatedAt { get; init; }
}

/// <summary>
/// Where a recovery pass's resolution ends and what schedule position it leaves behind. Two of these are planned up
/// front — one for each answer to "did the walk establish a run?" — because that answer is only known after the
/// fenced writes have been attempted.
/// </summary>
[PublicAPI]
public sealed record CronRecoveryResolution
{
    /// <summary>Exclusive lower bound of the rows this pass retires.</summary>
    public required DateTime RetireFromExclusiveUtc { get; init; }

    /// <summary>
    /// Inclusive upper bound of the rows this pass retires. Stops at the examined prefix when a saturated coalesce
    /// evaluation found no run: an unexamined row beyond it is the next pass's only coalesce candidate, and retiring
    /// it would drop the run the backlog is still owed.
    /// </summary>
    public required DateTime RetireThroughInclusiveUtc { get; init; }

    /// <summary>The watermark to persist.</summary>
    public required DateTime ReconciledThroughUtc { get; init; }

    /// <summary>The projection to persist.</summary>
    public required DateTime NextDueUtc { get; init; }
}

/// <summary>
/// The complete recovery decision, as a value. Says which instant to materialize at, which existing row to repurpose,
/// which to step past, and where the resolution window ends — and nothing about how any of it is written.
/// </summary>
/// <remarks>
/// The set of rows to retire is expressed as a bound rather than as identities on purpose. When a saturated coalesce
/// pass DOES establish its run inside the examined prefix, the resolution extends past the inspected window to the
/// full recovery instant, so it covers rows the snapshot never contained. The bound plus each provider's
/// still-claimable predicate is therefore the only faithful expression of the set.
/// </remarks>
[PublicAPI]
public sealed record CronRecoveryPlan
{
    /// <summary>Ordered attempts at the coalesced run; empty under <c>Skip</c>, or when every missed instant is accounted for.</summary>
    public required IReadOnlyList<CronRecoveryRunStep> RunSteps { get; init; }

    /// <summary>The resolution to apply once a step succeeded.</summary>
    public required CronRecoveryResolution WhenRunEstablished { get; init; }

    /// <summary>The resolution to apply when no step did — or when there were none.</summary>
    public required CronRecoveryResolution WhenNoRunEstablished { get; init; }
}
