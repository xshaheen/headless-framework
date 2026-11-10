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
    public const string OccurrenceEdit = "An occurrence follows its cron job — edit the schedule instead";
    public const string RunningEdit = "Running — it can no longer be edited";
    public const string FinishedEdit = "Already finished — duplicate it instead";
    public const string ClaimedEdit = "Claimed with its chain — it can no longer be edited";
    public const string KeyedEdit = "Keyed runs change through their key";
    public const string ChainStepEdit = "A chain step runs after its parent — edit the chain's first step";

    public const string UnsupportedContractEdit =
        "Stored under another contract version — its request cannot be rewritten";

    public const string UnsupportedContractDuplicate =
        "Stored under another contract version — this host cannot run a copy";

    public const string OccurrenceDuplicate = "Only a time job can be duplicated — run its cron job now instead";
    public const string UnfinishedDuplicate = "Not finished yet";
    public const string ChainDuplicate = "Part of a chain — chains are not duplicated";
    public const string FunctionNotRegistered = "Its function is no longer registered on this host";
    public const string OccurrenceForceDelete = "An occurrence cannot be force deleted — pause its cron job instead";
    public const string NotRunningForceDelete = "Not running — delete it instead";
    public const string CancelFirst = "Cancel first";
    public const string ChainForceDelete = "Part of a chain — chain steps are not deleted one by one";
    public const string RunningCronDelete = "Running — wait or cancel the occurrence";

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

    /// <summary>
    /// Edit follows <c>TimeJobEditGuard</c>, the rule the store applies: an idle unclaimed or a queued time job. A
    /// keyed job changes only through its key, and a chain step's due time comes from its parent.
    /// </summary>
    public static string? Edit(JobRunSummary run)
    {
        if (run.Kind == JobRunKind.CronJobOccurrence)
        {
            return OccurrenceEdit;
        }

        if (run.BusinessKey is not null)
        {
            return KeyedEdit;
        }

        if (run.ParentId is not null)
        {
            return ChainStepEdit;
        }

        return run.Status switch
        {
            JobStatus.Queued => null,
            JobStatus.Idle => run.IsLeased ? ClaimedEdit : null,
            JobStatus.InProgress => RunningEdit,
            _ => FinishedEdit,
        };
    }

    // A chain copied step by step would lose the links between its steps, so only a standalone run is duplicated.
    public static string? Duplicate(JobRunSummary run, bool isChainMember)
    {
        if (run.Kind == JobRunKind.CronJobOccurrence)
        {
            return OccurrenceDuplicate;
        }

        if (run.Status is JobStatus.Idle or JobStatus.Queued or JobStatus.InProgress)
        {
            return UnfinishedDuplicate;
        }

        return isChainMember ? ChainDuplicate : null;
    }

    // Force delete removes the stored row of a run whose cancel was requested but which has not stopped. It keeps the
    // protections Delete has, keyed runs included, and adds chains: a running step's tree cannot be removed in part.
    public static string? ForceDelete(JobRunSummary run, bool isChainMember)
    {
        if (run.Kind == JobRunKind.CronJobOccurrence)
        {
            return OccurrenceForceDelete;
        }

        if (run.BusinessKey is not null)
        {
            return KeyedDelete;
        }

        if (isChainMember)
        {
            return ChainForceDelete;
        }

        if (run.Status != JobStatus.InProgress)
        {
            return NotRunningForceDelete;
        }

        return run.CancelRequested ? null : CancelFirst;
    }

    // A definition removed from code is retired, not defined in code, so an operator can still clean it up.
    public static string? DeleteCron(CronJobEntity cronJob, bool hasRunningOccurrence)
    {
        if (JobsSeedId.IsSeedMarker(cronJob.InitIdentifier) && !JobsSeedId.IsRetiredMarker(cronJob.InitIdentifier))
        {
            return DefinedInCode;
        }

        return hasRunningOccurrence ? RunningCronDelete : null;
    }

    // JobRunSummary carries no generation flag: a keyed run in the run list is assumed current, and the store's own
    // requeue rule decides the rest.
    private static bool? _IsCurrent(JobRunSummary run) => run.BusinessKey is null ? null : true;
}
