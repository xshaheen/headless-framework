// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Linq.Expressions;

namespace Headless.Jobs;

/// <summary>
/// The requeue eligibility rules and the actionable-failure rule built on them. Every provider applies these, in its
/// writes and in its queries, so providers refuse the same rows for the same reason, and a count of actionable
/// failures never includes a row a requeue would refuse.
/// </summary>
/// <remarks>
/// <para>
/// A run is requeueable only from a finished state that did not succeed: <c>Failed</c>, <c>Cancelled</c>, or
/// <c>Skipped</c>. A time job is requeueable when it is in one of those states, is not a chain member (it has no parent and no children), and,
/// when keyed, is its key's current generation. Chain members are refused because a requeued child waits for a parent
/// run that never comes, and a requeued parent re-runs against children that already resolved against its failure. A
/// superseded keyed generation is refused because it would run beside the key's current generation.
/// </para>
/// <para>
/// A cron occurrence is requeueable when it is in one of those states; its overlap and instant checks read sibling
/// rows and are decided by the write itself.
/// </para>
/// <para>
/// Only a <c>Failed</c> run is an actionable failure: a cancelled or skipped run was stopped on purpose, so it asks
/// nothing of an operator even though a requeue accepts it. A failed occurrence stops being actionable once a later
/// occurrence of the same cron job succeeds: the schedule has recovered, so the failure no longer asks anything of an
/// operator. A failed time job stays actionable until it is requeued or deleted.
/// </para>
/// </remarks>
[PublicAPI]
public static class JobRequeueRules
{
    /// <summary>The refusal for a time job in the given state, or <see langword="null"/> when it may be requeued.</summary>
    /// <param name="status">The row's current status.</param>
    /// <param name="isChainMember">Whether the row has a parent or at least one child.</param>
    /// <param name="businessKey">The row's business key; <see langword="null"/> for an ordinary job.</param>
    /// <param name="isCurrentGeneration">Whether a keyed row is its key's current generation.</param>
    /// <returns>
    /// The refusal, checked in this order: not failed, cancelled, or skipped; chain member; superseded generation.
    /// </returns>
    public static JobRequeueOutcome? RefuseTimeJob(
        JobStatus status,
        bool isChainMember,
        string? businessKey,
        bool? isCurrentGeneration
    )
    {
        if (!IsRequeueableStatus(status))
        {
            return JobRequeueOutcome.NotFailed;
        }

        if (isChainMember)
        {
            return JobRequeueOutcome.ChainMember;
        }

        if (businessKey is not null && isCurrentGeneration != true)
        {
            return JobRequeueOutcome.SupersededGeneration;
        }

        return null;
    }

    /// <summary>
    /// The refusal a cron occurrence's own row decides, or <see langword="null"/> when the row may be requeued. The
    /// overlap and instant checks, which read sibling rows, are left to the requeue write.
    /// </summary>
    /// <param name="status">The occurrence's current status.</param>
    public static JobRequeueOutcome? RefuseCronJobOccurrence(JobStatus status)
    {
        return IsRequeueableStatus(status) ? null : JobRequeueOutcome.NotFailed;
    }

    /// <summary>
    /// Whether a run in <paramref name="status"/> may be requeued as far as its status decides: <c>Failed</c>,
    /// <c>Cancelled</c>, or <c>Skipped</c>.
    /// </summary>
    /// <param name="status">The run's status.</param>
    public static bool IsRequeueableStatus(JobStatus status)
    {
        return status is JobStatus.Failed or JobStatus.Cancelled or JobStatus.Skipped;
    }

    /// <summary>
    /// Whether a time job is an actionable failure: <c>Failed</c>, and requeueable by <see cref="RefuseTimeJob"/>.
    /// </summary>
    /// <param name="status">The row's current status.</param>
    /// <param name="isChainMember">Whether the row has a parent or at least one child.</param>
    /// <param name="businessKey">The row's business key; <see langword="null"/> for an ordinary job.</param>
    /// <param name="isCurrentGeneration">Whether a keyed row is its key's current generation.</param>
    public static bool IsActionableTimeJobFailure(
        JobStatus status,
        bool isChainMember,
        string? businessKey,
        bool? isCurrentGeneration
    )
    {
        return status == JobStatus.Failed
            && RefuseTimeJob(status, isChainMember, businessKey, isCurrentGeneration) is null;
    }

    /// <summary>Whether a finished run counts as a success: <c>Succeeded</c>, or <c>DueDone</c>.</summary>
    /// <param name="status">The run's status.</param>
    public static bool IsSuccess(JobStatus status)
    {
        return status is JobStatus.Succeeded or JobStatus.DueDone;
    }

    /// <summary>
    /// Whether a cron occurrence is an actionable failure: <c>Failed</c>, and no later occurrence of its cron job has
    /// succeeded.
    /// </summary>
    /// <param name="status">The occurrence's status.</param>
    /// <param name="laterOccurrenceSucceeded">
    /// Whether an occurrence of the same cron job with a later execution time succeeded.
    /// </param>
    public static bool IsActionableCronJobOccurrenceFailure(JobStatus status, bool laterOccurrenceSucceeded)
    {
        return status == JobStatus.Failed && !laterOccurrenceSucceeded;
    }

    /// <summary>The requeue rule for time jobs as a query predicate.</summary>
    /// <param name="timeJobs">The time-job set the predicate's chain-member check reads.</param>
    /// <typeparam name="TTimeJob">The time-job entity type.</typeparam>
    /// <returns>A predicate a query provider can translate.</returns>
    public static Expression<Func<TTimeJob, bool>> RequeueableTimeJob<TTimeJob>(IQueryable<TTimeJob> timeJobs)
        where TTimeJob : TimeJobEntity<TTimeJob>
    {
        return x =>
            (x.Status == JobStatus.Failed || x.Status == JobStatus.Cancelled || x.Status == JobStatus.Skipped)
            && x.ParentId == null
            && !timeJobs.Any(child => child.ParentId == x.Id)
            && (x.BusinessKey == null || x.IsCurrentGeneration == true);
    }

    /// <summary>
    /// The actionable-failure rule for time jobs as a query predicate: <c>Failed</c>, and requeueable.
    /// </summary>
    /// <param name="timeJobs">The time-job set the predicate's chain-member check reads.</param>
    /// <typeparam name="TTimeJob">The time-job entity type.</typeparam>
    /// <returns>A predicate a query provider can translate.</returns>
    public static Expression<Func<TTimeJob, bool>> ActionableTimeJobFailure<TTimeJob>(IQueryable<TTimeJob> timeJobs)
        where TTimeJob : TimeJobEntity<TTimeJob>
    {
        return x =>
            x.Status == JobStatus.Failed
            && x.ParentId == null
            && !timeJobs.Any(child => child.ParentId == x.Id)
            && (x.BusinessKey == null || x.IsCurrentGeneration == true);
    }

    /// <summary>The requeue rule a cron occurrence's own row decides, as a query predicate.</summary>
    /// <typeparam name="TCronJob">The cron-job entity type.</typeparam>
    /// <returns>A predicate a query provider can translate.</returns>
    public static Expression<Func<CronJobOccurrenceEntity<TCronJob>, bool>> RequeueableCronJobOccurrence<TCronJob>()
        where TCronJob : CronJobEntity
    {
        return x => x.Status == JobStatus.Failed || x.Status == JobStatus.Cancelled || x.Status == JobStatus.Skipped;
    }

    /// <summary>
    /// The actionable-failure rule for cron occurrences as a query predicate: <c>Failed</c>, and no occurrence of the
    /// same cron job with a later execution time succeeded.
    /// </summary>
    /// <param name="occurrences">The occurrence set the later-success check reads.</param>
    /// <typeparam name="TCronJob">The cron-job entity type.</typeparam>
    /// <returns>A predicate a query provider can translate.</returns>
    public static Expression<
        Func<CronJobOccurrenceEntity<TCronJob>, bool>
    > ActionableCronJobOccurrenceFailure<TCronJob>(IQueryable<CronJobOccurrenceEntity<TCronJob>> occurrences)
        where TCronJob : CronJobEntity
    {
        return x =>
            x.Status == JobStatus.Failed
            && !occurrences.Any(later =>
                later.CronJobId == x.CronJobId
                && later.ExecutionTime > x.ExecutionTime
                && (later.Status == JobStatus.Succeeded || later.Status == JobStatus.DueDone)
            );
    }
}
