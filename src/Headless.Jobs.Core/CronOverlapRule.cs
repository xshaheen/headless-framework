// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Linq.Expressions;
using Headless.Jobs.Entities;
using Headless.Jobs.Enums;

namespace Headless.Jobs;

/// <summary>
/// The single overlap rule, shared by every provider and by both the materialization and the recovery path so no two
/// of them can disagree about whether an earlier occurrence is still unfinished.
/// </summary>
/// <remarks>
/// Providers apply the rule while holding the definition's materialization mutex: the fenced definition update in the
/// relational providers, the per-definition lock in memory. Every new scheduled occurrence of a definition is created
/// under that mutex, so no second one can appear between the check and the write. An earlier occurrence finishing
/// concurrently can only make the check stale in the conservative direction — a skip that a later read would not have
/// made — never let two occurrences run together.
/// </remarks>
internal static class CronOverlapRule
{
    /// <summary>The <c>SkippedReason</c> stamped on an occurrence the overlap policy skipped.</summary>
    internal const string SkippedReason = "Skipped by overlap policy: an earlier occurrence was still unfinished";

    /// <summary>
    /// Whether <paramref name="policy" /> forbids a new occurrence while an earlier one is unfinished.
    /// </summary>
    /// <param name="policy">The persisted policy of the definition being materialized.</param>
    /// <remarks>
    /// Only <see cref="CronOverlapPolicy.Allow" /> permits overlap. A value this binary does not recognize, written
    /// by a newer one, is treated as forbidding it: skipping an occurrence is recoverable, running two at once may
    /// not be.
    /// </remarks>
    internal static bool ForbidsOverlap(CronOverlapPolicy policy) => policy != CronOverlapPolicy.Allow;

    /// <summary>
    /// Matches occurrences of <paramref name="cronJobId" /> that are still unfinished: <c>Idle</c>, <c>Queued</c>,
    /// or <c>InProgress</c>, lease state notwithstanding.
    /// </summary>
    /// <param name="cronJobId">The definition whose occurrences are examined.</param>
    /// <typeparam name="TCronJob">The concrete cron definition type the occurrence belongs to.</typeparam>
    /// <remarks>
    /// An idle row waiting to re-run after its node died, and an in-progress row whose lease lapsed before the
    /// reclaim sweep resolved it, both match: under <see cref="NodeDeathPolicy.Retry" /> either one runs again, so
    /// treating it as finished would start the next occurrence alongside it.
    /// </remarks>
    internal static Expression<Func<CronJobOccurrenceEntity<TCronJob>, bool>> UnfinishedOccurrenceOf<TCronJob>(
        Guid cronJobId
    )
        where TCronJob : CronJobEntity
    {
        return x =>
            x.CronJobId == cronJobId
            && (x.Status == JobStatus.Idle || x.Status == JobStatus.Queued || x.Status == JobStatus.InProgress);
    }

    /// <summary>
    /// The in-memory form of <see cref="UnfinishedOccurrenceOf{TCronJob}" />'s status test, for providers holding
    /// materialized entities. Kept beside the expression so the two status sets cannot drift apart.
    /// </summary>
    /// <param name="status">The occurrence's current status.</param>
    internal static bool IsUnfinished(JobStatus status) =>
        status is JobStatus.Idle or JobStatus.Queued or JobStatus.InProgress;
}
