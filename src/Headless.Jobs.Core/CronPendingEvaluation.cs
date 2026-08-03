// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Jobs;

/// <summary>
/// What a definition's schedule owes as of one store instant: the occurrences that fall at or before it which the
/// watermark has not yet passed, and whether that backlog is a misfire rather than ordinary lateness.
/// </summary>
/// <remarks>
/// Pending-ness is a property of the schedule and the watermark alone — an instant is pending whether or not an
/// occurrence row was ever materialized for it. That is the whole point of a durable watermark: a process that died
/// mid-sleep left no row behind, so a row-based definition of "missed" could not see it.
/// </remarks>
internal readonly record struct CronPendingEvaluation
{
    /// <summary>
    /// First occurrence after the watermark, or <see langword="null"/> when nothing is pending. Always exact, even when
    /// the count saturated, because it is the first instant the walk visits.
    /// </summary>
    public DateTime? EarliestPendingUtc { get; init; }

    /// <summary>Last pending instant the walk reached. Equals the earliest when exactly one is pending.</summary>
    public DateTime? LatestPendingUtc { get; init; }

    /// <summary>Pending occurrences counted. A lower bound when <see cref="CountSaturated"/> is set.</summary>
    public int PendingCount { get; init; }

    /// <summary>Whether the walk stopped at the evaluation ceiling with more pending instants beyond it.</summary>
    public bool CountSaturated { get; init; }

    /// <summary>
    /// Every pending instant the walk visited, in schedule order — the first element is
    /// <see cref="EarliestPendingUtc"/>. Bounded by the evaluation ceiling. Coalesce recovery walks this list to
    /// find the earliest instant not already accounted for by an executing or terminal occurrence.
    /// </summary>
    public IReadOnlyList<DateTime> PendingInstantsUtc { get; init; }

    /// <summary>
    /// Whether this backlog is a misfire: more than one pending instant, or a single one older than the definition's
    /// grace threshold. Decided from the watermark, never from a complete count.
    /// </summary>
    public bool IsRecovery { get; init; }

    /// <summary>Nothing pending — the schedule is fully reconciled as of the evaluated instant.</summary>
    public static CronPendingEvaluation None => new() { PendingInstantsUtc = [] };
}
