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

    /// <summary>Registered function name. Named to match <see cref="JobManagerDispatchContext.FunctionName"/>, which
    /// this projection is converted into once a candidate advances.</summary>
    public required string FunctionName { get; init; }

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

    /// <summary>
    /// The definition's own persisted misfire grace, so every node evaluates the same threshold for it rather than
    /// each applying its local configuration.
    /// </summary>
    public required int MissedRunGraceSeconds { get; init; }

    /// <summary>The definition's persisted recovery policy, applied when its watermark falls behind.</summary>
    public required MissedRunPolicy OnMissedRun { get; init; }

    /// <summary>
    /// Fingerprint of the rules this definition's projection was derived under, or <see langword="null"/> when it was
    /// positioned before fingerprinting existed. Compared for equality only; a mismatch means the same expression and
    /// timezone would now resolve to a different instant.
    /// </summary>
    public string? EvaluationFingerprint { get; init; }

    /// <summary>Consecutive deterministic evaluation failures used by the durable defer backoff.</summary>
    public int FingerprintFailureCount { get; init; }

    /// <summary>Provider-time retry boundary for a previously deferred definition.</summary>
    public DateTime? FingerprintRetryAfterUtc { get; init; }
}

/// <summary>
/// The keyset position a bounded candidate read resumes from: the ordering key of the last candidate the caller has
/// already examined and rejected.
/// </summary>
/// <remarks>
/// Exists so a caller that must exclude definitions can push the exclusion into the query INSTEAD of filtering an
/// already-truncated page. Filtering after the read starves: a page whose entries the caller all rejects empties on
/// every poll, and a healthy later definition never enters the window (#830). Resuming past the rejected page keeps the
/// bound on each read while still reaching every definition.
/// <para>
/// The cursor is the provider's own ordering key — <c>(NextDueUtc, CronJobId)</c> — and is opaque and provider-scoped:
/// backends order identifiers differently, so a cursor is only meaningful to the provider that produced it. Because the
/// pair is unique, a resumed read strictly advances and paging terminates at the last definition.
/// </para>
/// </remarks>
/// <param name="NextDueUtc">Projection of the last examined candidate.</param>
/// <param name="CronJobId">Identifier of the last examined candidate, breaking ties at the same projection.</param>
[PublicAPI]
public readonly record struct CronDispatchCandidateCursor(DateTime NextDueUtc, Guid CronJobId);

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
    /// <summary>Non-paused, non-deferred definitions ordered by projection, earliest first.</summary>
    public required IReadOnlyList<CronDispatchCandidate> Candidates { get; init; }

    /// <summary>The store's instant at the moment the candidates were read.</summary>
    public required DateTime StoreUtcNow { get; init; }
}
