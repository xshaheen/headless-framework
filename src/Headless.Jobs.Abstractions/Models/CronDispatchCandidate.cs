// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Jobs.Entities;
using Headless.Jobs.Enums;

namespace Headless.Jobs.Models;

/// <summary>
/// A cron definition as the scheduler's selection path sees it: the dispatch fields plus the schedule position the
/// advance fences on. Read from the indexed projection rather than derived, so selecting what is due costs an indexed
/// range scan instead of evaluating every definition's expression on every node.
/// </summary>
[PublicAPI]
public sealed record CronDispatchCandidate
{
    /// <summary>Identifier of the cron definition.</summary>
    public required Guid CronJobId { get; init; }

    /// <summary>Registered function name.</summary>
    public required string Function { get; init; }

    /// <summary>Six-field NCrontab expression. Evaluated only for a definition that actually advances.</summary>
    public required string Expression { get; init; }

    /// <summary>Optional IANA timezone identifier used to evaluate <see cref="Expression"/>.</summary>
    public string? TimeZoneId { get; init; }

    /// <summary>The definition version the advance must be fenced on.</summary>
    public required long ScheduleRevision { get; init; }

    /// <summary>The watermark the advance must be fenced on.</summary>
    public required DateTime ReconciledThroughUtc { get; init; }

    /// <summary>The projected instant of the first occurrence after the watermark — the indexed dispatch key.</summary>
    public required DateTime NextDueUtc { get; init; }

    /// <summary>Maximum retry attempts, carried onto materialized occurrences.</summary>
    public required int Retries { get; init; }

    /// <summary>Per-attempt retry delays in seconds, carried onto materialized occurrences.</summary>
    public int[]? RetryIntervals { get; init; }

    /// <summary>Node-death policy, carried onto materialized occurrences.</summary>
    public required NodeDeathPolicy OnNodeDeath { get; init; }
}

/// <summary>
/// The earliest cron definitions by projection, together with the instant the STORE evaluated them against.
/// </summary>
/// <remarks>
/// <see cref="StoreUtcNow"/> is read in the same statement as the projections, so comparing a candidate's
/// <see cref="CronDispatchCandidate.NextDueUtc"/> against it is a store-side decision even though the comparison runs
/// in the caller — both values come from one server snapshot, and neither carries the calling node's clock. The
/// advance re-asserts due-ness inside its own atomic statement, so this comparison selects work rather than
/// authorizing it.
/// </remarks>
[PublicAPI]
public sealed record CronDispatchCandidates
{
    /// <summary>Non-paused definitions ordered by projection, earliest first.</summary>
    public required IReadOnlyList<CronDispatchCandidate> Candidates { get; init; }

    /// <summary>The store's instant at the moment the candidates were read.</summary>
    public required DateTime StoreUtcNow { get; init; }
}
