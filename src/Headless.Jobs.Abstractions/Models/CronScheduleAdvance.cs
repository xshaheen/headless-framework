// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Jobs.Entities;

namespace Headless.Jobs.Models;

/// <summary>
/// Compare-and-advance request for one cron definition's schedule position — the watermark
/// (<see cref="CronJobEntity.ReconciledThroughUtc"/>) and the projection derived from it
/// (<see cref="CronJobEntity.NextDueUtc"/>).
/// </summary>
/// <remarks>
/// The fence is the observed watermark, the observed schedule revision, and a non-paused definition, so concurrent
/// nodes advancing from the same observed position produce exactly one winner and a node holding a stale definition
/// snapshot cannot advance at all. A caller that loses the fence is told by a <see langword="null"/> result, not by
/// an exception — losing a dispatch race is the expected outcome on every node but one.
/// </remarks>
[PublicAPI]
public sealed record CronScheduleAdvance
{
    /// <summary>The cron definition to advance.</summary>
    public required Guid CronJobId { get; init; }

    /// <summary>The exact durable watermark the caller observed. The advance is rejected if it no longer matches.</summary>
    public required DateTime ObservedReconciledThroughUtc { get; init; }

    /// <summary>The exact durable schedule revision the caller observed.</summary>
    public required long ExpectedScheduleRevision { get; init; }

    /// <summary>The watermark to persist — the instant through which the schedule is now reconciled.</summary>
    public required DateTime ReconciledThroughUtc { get; init; }

    /// <summary>The projection to persist — the first occurrence after <see cref="ReconciledThroughUtc"/>.</summary>
    public required DateTime NextDueUtc { get; init; }

    /// <summary>
    /// Fingerprint of the rules the new projection was derived under, or <see langword="null"/> to leave the persisted
    /// value untouched.
    /// </summary>
    /// <remarks>
    /// Written with the position rather than separately, so the two can never disagree: a projection is only ever
    /// meaningful alongside the rules that produced it, and a fingerprint refreshed independently would claim a
    /// position was current under rules it was never derived under.
    /// </remarks>
    public string? EvaluationFingerprint { get; init; }

    /// <summary>
    /// Whether the advance additionally requires the observed projection to be due against the <i>store's</i> clock.
    /// </summary>
    /// <remarks>
    /// Dispatch sets this so a node whose wall clock runs ahead of the database cannot advance a definition the store
    /// does not yet consider due — the same re-assertion the claim path performs when it repeats its eligibility
    /// predicate inside the atomic claim. Selection paths that are deliberately independent of due-ness (the
    /// evaluation-fingerprint sweep rebases stale definitions whether or not they are due) leave it
    /// <see langword="false"/>.
    /// </remarks>
    public bool RequireProjectionDue { get; init; }
}

/// <summary>Committed schedule position read back after a successful advance, plus the store's own clock.</summary>
/// <remarks>
/// Every value here is read back from the store rather than echoed from the request, so no caller re-derives a value
/// the database already decided — relational providers truncate instants to their column precision (PostgreSQL
/// microseconds, SQL Server <c>datetime2</c> ticks), and a caller that assumed its own value would drift from durable
/// state. <see cref="StoreUtcNow"/> is the authority for the grace comparison and for deriving the next position.
/// </remarks>
[PublicAPI]
public sealed record CronScheduleAdvanceResult
{
    /// <summary>The persisted watermark.</summary>
    public required DateTime ReconciledThroughUtc { get; init; }

    /// <summary>The persisted projection.</summary>
    public required DateTime NextDueUtc { get; init; }

    /// <summary>The store's current instant, evaluated by the store rather than by the calling node.</summary>
    public required DateTime StoreUtcNow { get; init; }
}
