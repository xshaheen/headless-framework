// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Jobs;

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
