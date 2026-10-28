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
    internal const string TagSweepDeferralReason = "reason";

    internal const string SweepDeferralFleetDisagreement = "fleet_disagreement";

    internal const string SweepDeferralMembershipUnavailable = "membership_unavailable";

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

    /// <summary>Runs that reached a terminal outcome, whether or not the handler was invoked.</summary>
    internal const string ExecutionOutcomesName = "headless.jobs.execution.outcomes";

    /// <summary>Attempts a run made beyond its first.</summary>
    internal const string ExecutionRetriesName = "headless.jobs.execution.retries";

    /// <summary>How late a run started after the instant it was due, in seconds.</summary>
    internal const string QueueLagName = "headless.jobs.queue.lag";

    /// <summary>Rows a claim selected and lost to another node.</summary>
    internal const string ClaimConflictsName = "headless.jobs.claim.conflicts";

    /// <summary>The run's terminal outcome: <c>succeeded</c>, <c>failed</c>, <c>cancelled</c>, or <c>skipped</c>.</summary>
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
        description: "Job runs that reached a terminal outcome, including runs whose handler was never invoked."
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

    internal static void FingerprintSweepDeferred(string reason)
    {
        if (!_FingerprintSweepDeferred.Enabled)
        {
            return;
        }

        _FingerprintSweepDeferred.Add(1, new TagList { { TagSweepDeferralReason, reason } });
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
    /// Records a run that reached <paramref name="status" />. A non-terminal status records nothing: the row is left
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
