// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Jobs;

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
