// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace Headless.Jobs;

/// <summary>
/// Metric instruments for the Jobs subsystem, registered against <see cref="JobsDiagnostics.Meter" />. There is no
/// OpenTelemetry semantic convention for job scheduling, so instrument and attribute names use the bespoke
/// <c>headless.jobs.*</c> namespace, as every Headless subsystem does for names no semantic convention defines.
/// </summary>
internal static class JobsMetrics
{
    /// <summary>Coordinated post-commit signals dropped before the worker could run them.</summary>
    internal const string PostCommitSignalsDroppedName = "headless.jobs.post_commit_signals.dropped";

    /// <summary>Why a signal was dropped: <c>full</c> (channel at capacity) or <c>stopping</c> (host shutting down).</summary>
    internal const string TagDropReason = "headless.jobs.drop_reason";

    /// <summary>Cron fingerprint sweep passes that rebased nothing because fleet agreement could not be shown.</summary>
    internal const string FingerprintSweepDeferredName = "headless.jobs.cron.fingerprint_sweep.deferred";

    /// <summary>
    /// Why a sweep pass deferred: <c>fleet_disagreement</c> or <c>membership_unavailable</c>. Carries no node identity,
    /// which would make the series unbounded; the holdout warning names the nodes instead.
    /// </summary>
    internal const string TagSweepDeferralReason = "headless.jobs.deferral_reason";

    /// <summary>Due cron occurrences recorded as skipped instead of running.</summary>
    internal const string CronOccurrencesSkippedName = "headless.jobs.cron.occurrences.skipped";

    /// <summary>
    /// Why the occurrences were skipped: <c>overlap</c> (an earlier occurrence was still unfinished) or
    /// <c>missed_run</c> (a missed-run recovery retired them).
    /// </summary>
    internal const string TagSkipReason = "headless.jobs.skip_reason";

    /// <summary>
    /// The job function a measurement is about, bounded by the host's registered functions (see
    /// <see cref="JobsFunctionTagBound" />).
    /// </summary>
    internal const string TagFunction = "headless.jobs.function";

    internal const string SkipReasonOverlap = "overlap";

    internal const string SkipReasonMissedRun = "missed_run";

    /// <summary>How long a run took, from its first attempt to its outcome, in seconds.</summary>
    internal const string ExecutionDurationName = "headless.jobs.execution.duration";

    /// <summary>
    /// Runs that stored a terminal outcome, whether or not the handler was invoked, and runs that ended because they lost
    /// their lease.
    /// </summary>
    internal const string ExecutionOutcomesName = "headless.jobs.execution.outcomes";

    /// <summary>Terminal writes the store refused because the run no longer owned its row.</summary>
    internal const string CompletionRejectedName = "headless.jobs.completion.rejected";

    /// <summary>Rows a reclaim sweep moved out of a lease their owner no longer held.</summary>
    internal const string ReclaimsName = "headless.jobs.reclaims";

    /// <summary>
    /// What made the sweep reclaim the row: <c>lease_expired</c> (the lease lapsed on any node), <c>dead_node</c> (the
    /// membership declared the owner dead), or <c>orphaned_owner</c> (the owner is absent from the membership).
    /// </summary>
    internal const string TagReclaimReason = "headless.jobs.reclaim.reason";

    /// <summary>
    /// What the row's node-death policy did with it: <c>released</c> (back to <c>Idle</c>), <c>marked_failed</c>, or
    /// <c>skipped</c>.
    /// </summary>
    internal const string TagReclaimAction = "headless.jobs.reclaim.action";

    /// <summary>Attempts a run made beyond its first.</summary>
    internal const string ExecutionRetriesName = "headless.jobs.execution.retries";

    /// <summary>How late a run started after the instant it was due, in seconds.</summary>
    internal const string QueueLagName = "headless.jobs.queue.lag";

    /// <summary>Rows a claim selected and lost to another node.</summary>
    internal const string ClaimConflictsName = "headless.jobs.claim.conflicts";

    /// <summary>
    /// The run's outcome: <c>succeeded</c>, <c>failed</c>, <c>cancelled</c>, or <c>skipped</c> when the store holds that
    /// terminal status, or <c>lease_lost</c> when the run ended without owning its row. On
    /// <c>headless.jobs.completion.rejected</c> it is the outcome the refused write tried to store.
    /// </summary>
    internal const string TagOutcome = "headless.jobs.outcome";

    /// <summary>The fully qualified type of the exception behind a failed run, per the OpenTelemetry convention.</summary>
    internal const string TagErrorType = "error.type";

    /// <summary>
    /// What started the extra attempt: <c>in_process</c> (the failure policy retried it inside the run) or
    /// <c>recovery</c> (a reclaimed run resumed on a node after the previous attempt was lost).
    /// </summary>
    internal const string TagRetryKind = "headless.jobs.retry.kind";

    /// <summary>
    /// What made the run due: <c>scheduled</c>, <c>recovery</c> (a missed-run recovery, whose lag spans the outage), or
    /// <c>run_now</c> (dispatched the moment it was written).
    /// </summary>
    internal const string TagTrigger = "headless.jobs.trigger";

    /// <summary>The claim strategy that lost the row: <c>postgresql</c>, <c>sqlserver</c>, or <c>cas</c>.</summary>
    internal const string TagClaimStrategy = "headless.jobs.claim.strategy";

    internal const string OutcomeSucceeded = "succeeded";

    internal const string OutcomeFailed = "failed";

    internal const string OutcomeCancelled = "cancelled";

    internal const string OutcomeSkipped = "skipped";

    /// <summary>
    /// Not a terminal status: the run stopped because it lost its lease, and the row's node-death policy settles it on
    /// reclaim.
    /// </summary>
    internal const string OutcomeLeaseLost = "lease_lost";

    internal const string ReclaimReasonLeaseExpired = "lease_expired";

    internal const string ReclaimReasonDeadNode = "dead_node";

    internal const string ReclaimReasonOrphanedOwner = "orphaned_owner";

    internal const string ReclaimActionReleased = "released";

    internal const string ReclaimActionMarkedFailed = "marked_failed";

    internal const string ReclaimActionSkipped = "skipped";

    internal const string RetryKindInProcess = "in_process";

    internal const string RetryKindRecovery = "recovery";

    internal const string TriggerScheduled = "scheduled";

    internal const string TriggerRecovery = "recovery";

    internal const string TriggerRunNow = "run_now";

    internal const string ClaimStrategyPostgreSql = "postgresql";

    internal const string ClaimStrategySqlServer = "sqlserver";

    internal const string ClaimStrategyCas = "cas";

    // A job can finish in milliseconds or run for an hour, and a run can start late by an outage's length, so the
    // boundaries span both ends rather than stopping at the ten seconds the messaging conventions advise.
    internal static readonly IReadOnlyList<double> DurationBucketBoundaries =
    [
        0.005,
        0.01,
        0.025,
        0.05,
        0.1,
        0.25,
        0.5,
        1,
        2.5,
        5,
        10,
        30,
        60,
        120,
        300,
        600,
        1800,
        3600,
    ];

    private static readonly Counter<long> _CronOccurrencesSkipped = JobsDiagnostics.Meter.CreateCounter<long>(
        CronOccurrencesSkippedName,
        unit: "{occurrence}",
        description: "Due cron occurrences the scheduler recorded as skipped instead of running, by reason."
    );

    private static readonly Histogram<double> _ExecutionDuration = _CreateSecondsHistogram(
        ExecutionDurationName,
        "Duration of job runs whose handler was invoked, from the first attempt to the outcome, by outcome."
    );

    private static readonly Counter<long> _ExecutionOutcomes = JobsDiagnostics.Meter.CreateCounter<long>(
        ExecutionOutcomesName,
        unit: "{run}",
        description: "Job runs that stored a terminal outcome, including runs whose handler was never invoked, and runs "
            + "that ended on lease loss (outcome lease_lost, which is not a terminal status)."
    );

    private static readonly Counter<long> _CompletionRejected = JobsDiagnostics.Meter.CreateCounter<long>(
        CompletionRejectedName,
        unit: "{run}",
        description: "Terminal writes the store refused because the run no longer owned its row, by the outcome the "
            + "write tried to store."
    );

    private static readonly Counter<long> _Reclaims = JobsDiagnostics.Meter.CreateCounter<long>(
        ReclaimsName,
        unit: "{row}",
        description: "Rows reclaim sweeps moved out of a lease their owner no longer held, by reason and by what the "
            + "node-death policy did."
    );

    private static readonly Counter<long> _ExecutionRetries = JobsDiagnostics.Meter.CreateCounter<long>(
        ExecutionRetriesName,
        unit: "{attempt}",
        description: "Attempts job runs made beyond their first, in process or after a reclaim."
    );

    private static readonly Histogram<double> _QueueLag = _CreateSecondsHistogram(
        QueueLagName,
        "How late job runs started after the instant they were due, by what made them due."
    );

    private static readonly Counter<long> _ClaimConflicts = JobsDiagnostics.Meter.CreateCounter<long>(
        ClaimConflictsName,
        unit: "{row}",
        description: "Rows a claim selected and lost to another node, by claim strategy."
    );

    private static readonly Counter<long> _PostCommitSignalsDropped = JobsDiagnostics.Meter.CreateCounter<long>(
        PostCommitSignalsDroppedName,
        unit: "{signal}",
        description: "Coordinated post-commit signals dropped before the worker ran them; the poll sweep recovers the row."
    );

    private static readonly Counter<long> _FingerprintSweepDeferred = JobsDiagnostics.Meter.CreateCounter<long>(
        FingerprintSweepDeferredName,
        unit: "{pass}",
        description: "Cron fingerprint sweep passes that rebased nothing because fleet agreement could not be shown, "
            + "by reason."
    );

    internal static void FingerprintSweepDeferred(CronFingerprintSweepDeferralReason reason)
    {
        if (!_FingerprintSweepDeferred.Enabled)
        {
            return;
        }

        var tag = reason switch
        {
            CronFingerprintSweepDeferralReason.FleetDisagreement => "fleet_disagreement",
            CronFingerprintSweepDeferralReason.MembershipUnavailable => "membership_unavailable",
            _ => throw new ArgumentOutOfRangeException(nameof(reason), reason, "Only a deferred pass is counted."),
        };

        _FingerprintSweepDeferred.Add(1, new TagList { { TagSweepDeferralReason, tag } });
    }

    internal static void PostCommitSignalDropped(string reason)
    {
        if (!_PostCommitSignalsDropped.Enabled)
        {
            return;
        }

        _PostCommitSignalsDropped.Add(1, new TagList { { TagDropReason, reason } });
    }

    internal static void CronOccurrencesSkipped(
        JobsFunctionTagBound functions,
        string reason,
        string function,
        int count
    )
    {
        if (count <= 0 || !_CronOccurrencesSkipped.Enabled)
        {
            return;
        }

        _CronOccurrencesSkipped.Add(
            count,
            new TagList { { TagSkipReason, reason }, { TagFunction, functions.Tag(function) } }
        );
    }

    /// <summary>
    /// Records a run that ended because it lost its lease: the outcome is <c>lease_lost</c>, with no duration sample,
    /// because the row's node-death policy decides how the run ends.
    /// </summary>
    internal static void LeaseLost(JobsFunctionTagBound functions, string function)
    {
        if (!_ExecutionOutcomes.Enabled)
        {
            return;
        }

        _ExecutionOutcomes.Add(
            1,
            new TagList { { TagFunction, functions.Tag(function) }, { TagOutcome, OutcomeLeaseLost } }
        );
    }

    /// <summary>
    /// Records a terminal write of <paramref name="status" /> that matched no row: the run lost its row before the write
    /// committed, so the store keeps the reclaim's status. A non-terminal status records nothing.
    /// </summary>
    internal static void CompletionRejected(JobsFunctionTagBound functions, string function, JobStatus status)
    {
        if (!_CompletionRejected.Enabled || OutcomeOf(status) is not { } outcome)
        {
            return;
        }

        _CompletionRejected.Add(1, new TagList { { TagFunction, functions.Tag(function) }, { TagOutcome, outcome } });
    }

    /// <summary>Records one sweep's moved rows, one measurement per action that moved any.</summary>
    internal static void Reclaims(string reason, JobReclaimCounts counts)
    {
        if (counts.Total <= 0 || !_Reclaims.Enabled)
        {
            return;
        }

        _AddReclaims(reason, ReclaimActionReleased, counts.Released);
        _AddReclaims(reason, ReclaimActionMarkedFailed, counts.MarkedFailed);
        _AddReclaims(reason, ReclaimActionSkipped, counts.Skipped);
    }

    private static void _AddReclaims(string reason, string action, int count)
    {
        if (count > 0)
        {
            _Reclaims.Add(count, new TagList { { TagReclaimReason, reason }, { TagReclaimAction, action } });
        }
    }

    /// <summary>
    /// Records a run that stored <paramref name="status" />. Call it only once the terminal write matched its row. A
    /// non-terminal status records nothing: the row is left
    /// for recovery, and the run that finishes it records the outcome. <paramref name="duration" /> is
    /// <see langword="null" /> when the handler was never invoked, so such a run counts as an outcome but adds no
    /// duration sample.
    /// </summary>
    internal static void ExecutionCompleted(
        JobsFunctionTagBound functions,
        string function,
        JobStatus status,
        Exception? error,
        TimeSpan? duration
    )
    {
        if (!_ExecutionOutcomes.Enabled && !_ExecutionDuration.Enabled)
        {
            return;
        }

        if (OutcomeOf(status) is not { } outcome)
        {
            return;
        }

        var tags = new TagList { { TagFunction, functions.Tag(function) }, { TagOutcome, outcome } };
        if (error is not null && string.Equals(outcome, OutcomeFailed, StringComparison.Ordinal))
        {
            tags.Add(TagErrorType, ErrorTypeOf(error));
        }

        _ExecutionOutcomes.Add(1, tags);
        if (duration is { } elapsed)
        {
            _ExecutionDuration.Record(elapsed.TotalSeconds, tags);
        }
    }

    internal static void RetryStarted(string kind)
    {
        if (!_ExecutionRetries.Enabled)
        {
            return;
        }

        _ExecutionRetries.Add(1, new TagList { { TagRetryKind, kind } });
    }

    internal static void QueueLag(TimeSpan lag, string trigger)
    {
        if (!_QueueLag.Enabled)
        {
            return;
        }

        _QueueLag.Record(lag.TotalSeconds, new TagList { { TagTrigger, trigger } });
    }

    /// <summary>
    /// Whether anything listens to the claim-conflict counter, so a claim skips counting its lost rows when nothing does.
    /// </summary>
    internal static bool ClaimConflictsEnabled => _ClaimConflicts.Enabled;

    internal static void ClaimConflicts(string strategy, int count)
    {
        if (count <= 0 || !_ClaimConflicts.Enabled)
        {
            return;
        }

        _ClaimConflicts.Add(count, new TagList { { TagClaimStrategy, strategy } });
    }

    /// <summary>
    /// The one mapping from a job status to the outcome tag; <see langword="null" /> for a status that is not terminal.
    /// <see cref="JobStatus.DueDone" /> is a success the fallback sweep recorded, so it shares the success outcome.
    /// </summary>
    internal static string? OutcomeOf(JobStatus status)
    {
        return status switch
        {
            JobStatus.Succeeded or JobStatus.DueDone => OutcomeSucceeded,
            JobStatus.Failed => OutcomeFailed,
            JobStatus.Cancelled => OutcomeCancelled,
            JobStatus.Skipped => OutcomeSkipped,
            _ => null,
        };
    }

    // The fully qualified exception type, as the OpenTelemetry error.type convention asks for exceptions.
    internal static string ErrorTypeOf(Exception exception)
    {
        var type = exception.GetType();

        return type.FullName ?? type.Name;
    }

    private static Histogram<double> _CreateSecondsHistogram(string name, string description)
    {
        return JobsDiagnostics.Meter.CreateHistogram(
            name,
            unit: "s",
            description: description,
            tags: null,
            advice: new InstrumentAdvice<double> { HistogramBucketBoundaries = DurationBucketBoundaries }
        );
    }
}
