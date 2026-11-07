// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Jobs.Internal;

namespace Headless.Jobs;

/// <summary>
/// The one rule set that decides which Jobs commands an object allows now. Its storage half is
/// <see cref="JobRequeueRules"/>, which every provider also applies in its queries, so an allowed-actions answer, a
/// Needs-you count, and an Operation scope never disagree about a row; this class adds the cancel, delete, run-now,
/// pause, resume, and edit rules on top. Each method returns the refusal shown on the disabled control, or
/// <see langword="null"/> when the command is allowed.
/// </summary>
internal static class JobsRules
{
    public const string RunGone = "This run no longer exists";
    public const string CronJobGone = "This cron job no longer exists";
    public const string NotRequeueable = "Only a failed, cancelled, or skipped run can be requeued";
    public const string RunningRequeue = "Running — cancel instead";
    public const string ChainMember = "Part of a chain — chain steps are not requeued one by one";
    public const string SupersededGeneration = "A newer run of this key replaced it";
    public const string CancelPending = "Cancel requested — waiting for the job to stop";
    public const string Finished = "Already finished";
    public const string OccurrenceCancel = "An occurrence cannot be cancelled — pause its cron job instead";
    public const string RunningDelete = "Running — cancel it first";
    public const string KeyedDelete = "Keyed runs are kept — cancel the key instead";
    public const string Paused = "Paused — resume first";
    public const string OverlapForbidden = "Running — this cron job does not allow overlapping runs";
    public const string AlreadyPaused = "Already paused";
    public const string NotPaused = "Not paused";
    public const string Retired = "Removed from code — it can no longer be resumed";
    public const string DefinedInCode = "Defined in code";

    public static string? Requeue(JobRunSummary run, bool isChainMember)
    {
        if (run.Status is JobStatus.InProgress or JobStatus.Queued)
        {
            return RunningRequeue;
        }

        var refusal =
            run.Kind == JobRunKind.TimeJob
                ? JobRequeueRules.RefuseTimeJob(run.Status, isChainMember, run.BusinessKey, _IsCurrent(run))
                : JobRequeueRules.RefuseCronJobOccurrence(run.Status);

        return refusal switch
        {
            null => null,
            JobRequeueOutcome.ChainMember => ChainMember,
            JobRequeueOutcome.SupersededGeneration => SupersededGeneration,
            _ => NotRequeueable,
        };
    }

    // The stored row carries a cancel request without changing its status, so a running job reads InProgress until it
    // stops; the request itself is what refuses a second cancel.
    public static string? Cancel(JobRunSummary run)
    {
        if (run.Kind == JobRunKind.CronJobOccurrence)
        {
            return OccurrenceCancel;
        }

        if (run.Status is not (JobStatus.Idle or JobStatus.Queued or JobStatus.InProgress))
        {
            return Finished;
        }

        return run.CancelRequested ? CancelPending : null;
    }

    public static string? Delete(JobRunSummary run)
    {
        if (run.Status == JobStatus.InProgress)
        {
            return RunningDelete;
        }

        return run.Kind == JobRunKind.TimeJob && run.BusinessKey is not null ? KeyedDelete : null;
    }

    public static string? RunNow(CronJobEntity cronJob, bool hasUnfinishedOccurrence)
    {
        if (cronJob.IsPaused)
        {
            return Paused;
        }

        return CronOverlapRule.ForbidsOverlap(cronJob.OnOverlap) && hasUnfinishedOccurrence ? OverlapForbidden : null;
    }

    public static string? Pause(CronJobEntity cronJob) => cronJob.IsPaused ? AlreadyPaused : null;

    public static string? Resume(CronJobEntity cronJob)
    {
        if (!cronJob.IsPaused)
        {
            return NotPaused;
        }

        return JobsSeedId.IsRetiredMarker(cronJob.InitIdentifier) ? Retired : null;
    }

    public static string? EditCron(CronJobEntity cronJob)
    {
        return JobsSeedId.IsSeedMarker(cronJob.InitIdentifier) ? DefinedInCode : null;
    }

    /// <summary>Whether the cron job is a definition the seed step owns, so its schedule is read-only.</summary>
    public static bool IsDefinedInCode(CronJobEntity cronJob) => JobsSeedId.IsSeedMarker(cronJob.InitIdentifier);

    // JobRunSummary carries no generation flag: a keyed run in the run list is assumed current, and the store's own
    // requeue rule decides the rest.
    private static bool? _IsCurrent(JobRunSummary run) => run.BusinessKey is null ? null : true;
}
