// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Jobs.Enums;

/// <summary>
/// What a cron definition does when one of its occurrences becomes due while an earlier occurrence of the same
/// definition is still unfinished.
/// </summary>
/// <remarks>
/// An earlier occurrence is unfinished while it is <see cref="JobStatus.Idle"/>, <see cref="JobStatus.Queued"/>, or
/// <see cref="JobStatus.InProgress"/>. That deliberately includes an idle occurrence waiting to re-run after its node
/// died, and an in-progress one whose lease has lapsed but which the reclaim sweep has not yet resolved: both are
/// still the same logical run, and a <see cref="NodeDeathPolicy.Retry"/> reclaim would start it again next to the new
/// one.
/// <para>
/// Orthogonal to <see cref="MissedRunPolicy"/>, which decides how many runs a backlog produces, and to the function's
/// <c>MaxConcurrency</c>, which throttles executions of one function on one node. The overlap policy is decided once,
/// cluster-wide, when the occurrence is materialized; <c>MaxConcurrency</c> applies later, at admission.
/// </para>
/// <para>
/// Cancelling the running occurrence in favor of the new one (Kubernetes' <c>Replace</c>, Temporal's
/// <c>CANCEL_OTHER</c>) is not offered: cron occurrences have no durable cross-node cancellation to build it on.
/// </para>
/// </remarks>
[PublicAPI]
public enum CronOverlapPolicy
{
    /// <summary>Default. Occurrences are materialized on schedule whether or not an earlier one is unfinished.</summary>
    Allow = 0,

    /// <summary>
    /// An occurrence that becomes due while an earlier occurrence is unfinished is recorded as
    /// <see cref="JobStatus.Skipped"/> instead of running, and the schedule moves past it. The skipped instant counts
    /// as handled, so it is never replayed as a missed run.
    /// </summary>
    Skip = 1,
}
