// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Fencing;
using Headless.Jobs;
using Headless.Jobs.Infrastructure;

namespace Microsoft.EntityFrameworkCore;

public static class HeadlessJobsQueryExtensions
{
    /// <summary>
    /// Keeps only rows whose function this host runs. A no-op on an unfiltered host, so its queries keep claiming rows
    /// of every function, including ones it has not registered.
    /// </summary>
    internal static IQueryable<TTimeJob> WhereRunnable<TTimeJob>(this IQueryable<TTimeJob> q, JobsRunFilter filter)
        where TTimeJob : TimeJobEntity<TTimeJob>
    {
        return filter.RunnableFunctions is { } runnable ? q.Where(e => runnable.Contains(e.Function)) : q;
    }

    /// <inheritdoc cref="WhereRunnable{TTimeJob}(IQueryable{TTimeJob},JobsRunFilter)"/>
    internal static IQueryable<CronJobOccurrenceEntity<TCronJob>> WhereRunnable<TCronJob>(
        this IQueryable<CronJobOccurrenceEntity<TCronJob>> q,
        JobsRunFilter filter
    )
        where TCronJob : CronJobEntity
    {
        return filter.RunnableFunctions is { } runnable ? q.Where(e => runnable.Contains(e.Function)) : q;
    }

    /// <summary>
    /// Drops rows of cluster-limited functions. Immediate acquisition applies it so a limited run is always leased by
    /// the scheduler's claim, the one path that counts it against the limit.
    /// </summary>
    internal static IQueryable<TTimeJob> WhereNotClusterLimited<TTimeJob>(
        this IQueryable<TTimeJob> q,
        JobsClusterConcurrency limits
    )
        where TTimeJob : TimeJobEntity<TTimeJob>
    {
        var limited = limits.LimitedFunctions;
        return limited.Length != 0 ? q.Where(e => !limited.Contains(e.Function)) : q;
    }

    /// <inheritdoc cref="WhereNotClusterLimited{TTimeJob}(IQueryable{TTimeJob},JobsClusterConcurrency)"/>
    internal static IQueryable<CronJobOccurrenceEntity<TCronJob>> WhereNotClusterLimited<TCronJob>(
        this IQueryable<CronJobOccurrenceEntity<TCronJob>> q,
        JobsClusterConcurrency limits
    )
        where TCronJob : CronJobEntity
    {
        var limited = limits.LimitedFunctions;
        return limited.Length != 0 ? q.Where(e => !limited.Contains(e.Function)) : q;
    }

    /// <summary>Keeps only cron definitions whose function this host runs.</summary>
    internal static IQueryable<TCronJob> WhereDefinitionRunnable<TCronJob>(
        this IQueryable<TCronJob> q,
        JobsRunFilter filter
    )
        where TCronJob : CronJobEntity
    {
        return filter.RunnableFunctions is { } runnable ? q.Where(e => runnable.Contains(e.Function)) : q;
    }

    /// <summary>
    /// Selects the rows a claim by <paramref name="ownerId"/> may take: non-terminal, not yet running, and either
    /// already leased to that owner (a crashed node re-picking its own rows) or with no live lease, judged by the
    /// database clock inside the statement.
    /// </summary>
    /// <remarks>
    /// The no-live-lease arm holds for every <c>OnNodeDeath</c> policy: an <c>Idle</c> or <c>Queued</c> row never
    /// invoked user code (a run starts only after its fenced admission to <c>InProgress</c>), so reclaiming it starts
    /// the job's first run, and the previous attempt's admission then fails its generation fence.
    /// </remarks>
    internal static IQueryable<TTimeJob> WhereCanAcquireUsingDatabaseClock<TTimeJob>(
        this IQueryable<TTimeJob> q,
        string ownerId
    )
        where TTimeJob : TimeJobEntity<TTimeJob>
    {
        return q.Where(
            JobsLeaseExpressions.Expand<TTimeJob>(e =>
                (e.Status == JobStatus.Idle || e.Status == JobStatus.Queued)
                && (
                    (e.Lease.State == RowLeaseState.Active && e.Lease.Holder == ownerId)
                    || !JobsLeaseExpressions.IsLive(e.Lease)
                )
            )
        );
    }

    /// <inheritdoc cref="WhereCanAcquireUsingDatabaseClock{TTimeJob}(IQueryable{TTimeJob},string)"/>
    internal static IQueryable<CronJobOccurrenceEntity<TCronJob>> WhereCanAcquireUsingDatabaseClock<TCronJob>(
        this IQueryable<CronJobOccurrenceEntity<TCronJob>> q,
        string ownerId
    )
        where TCronJob : CronJobEntity
    {
        return q.Where(
            JobsLeaseExpressions.Expand<CronJobOccurrenceEntity<TCronJob>>(e =>
                (e.Status == JobStatus.Idle || e.Status == JobStatus.Queued)
                && (
                    (e.Lease.State == RowLeaseState.Active && e.Lease.Holder == ownerId)
                    || !JobsLeaseExpressions.IsLive(e.Lease)
                )
            )
        );
    }

    /// <summary>
    /// The time-job peek's predicate: the acquire predicate without its owner arm. A row this node already holds was
    /// dispatched by an earlier wake, so peeking it again would admit it to the pool twice; the claim keeps the owner
    /// arm so a crashed node still re-claims its own rows. Unlike the fallback predicate, an <c>Idle</c> row with a
    /// live foreign lease stays out, because no claim could take it.
    /// </summary>
    internal static IQueryable<TTimeJob> WhereCanPeekUsingDatabaseClock<TTimeJob>(this IQueryable<TTimeJob> q)
        where TTimeJob : TimeJobEntity<TTimeJob>
    {
        return q.Where(
            JobsLeaseExpressions.Expand<TTimeJob>(e =>
                (e.Status == JobStatus.Idle || e.Status == JobStatus.Queued) && !JobsLeaseExpressions.IsLive(e.Lease)
            )
        );
    }

    /// <summary>
    /// The stored cron-occurrence peek's predicate: the acquire predicate without its owner arm. An occurrence this
    /// node already holds was dispatched by an earlier wake and is waiting for a worker or a concurrency slot, so
    /// peeking it again would re-claim it and admit it to the pool once per scheduler poll.
    /// </summary>
    internal static IQueryable<CronJobOccurrenceEntity<TCronJob>> WhereCanPeekUsingDatabaseClock<TCronJob>(
        this IQueryable<CronJobOccurrenceEntity<TCronJob>> q
    )
        where TCronJob : CronJobEntity
    {
        return q.Where(
            JobsLeaseExpressions.Expand<CronJobOccurrenceEntity<TCronJob>>(e =>
                (e.Status == JobStatus.Idle || e.Status == JobStatus.Queued) && !JobsLeaseExpressions.IsLive(e.Lease)
            )
        );
    }

    /// <summary>
    /// Selects the rows the fallback sweep may claim: <c>Idle</c>, or <c>Queued</c> with no live lease. Unlike the
    /// acquire predicate it is owner-agnostic.
    /// </summary>
    internal static IQueryable<TTimeJob> WhereCanFallbackClaimUsingDatabaseClock<TTimeJob>(this IQueryable<TTimeJob> q)
        where TTimeJob : TimeJobEntity<TTimeJob>
    {
        return q.Where(
            JobsLeaseExpressions.Expand<TTimeJob>(e =>
                e.Status == JobStatus.Idle || (e.Status == JobStatus.Queued && !JobsLeaseExpressions.IsLive(e.Lease))
            )
        );
    }

    /// <inheritdoc cref="WhereCanFallbackClaimUsingDatabaseClock{TTimeJob}(IQueryable{TTimeJob})"/>
    internal static IQueryable<CronJobOccurrenceEntity<TCronJob>> WhereCanFallbackClaimUsingDatabaseClock<TCronJob>(
        this IQueryable<CronJobOccurrenceEntity<TCronJob>> q
    )
        where TCronJob : CronJobEntity
    {
        return q.Where(
            JobsLeaseExpressions.Expand<CronJobOccurrenceEntity<TCronJob>>(e =>
                e.Status == JobStatus.Idle || (e.Status == JobStatus.Queued && !JobsLeaseExpressions.IsLive(e.Lease))
            )
        );
    }

    /// <summary>
    /// The compare-and-advance fence for a cron definition's schedule position: the row still holds the watermark, the
    /// projection, and the schedule revision the caller observed, and is not paused. Advancement requires all four, so
    /// concurrent nodes advancing from the same observed position produce exactly one winner (the loser matches zero
    /// rows) and a node holding a stale definition snapshot cannot apply superseded settings.
    /// </summary>
    /// <remarks>
    /// The watermark equality is what serializes the racers — it is a value CAS, not a lock, so the losing node needs
    /// no rollback and raises nothing. The projection equality catches the one transition that keeps the watermark: a
    /// fingerprint rebase moves only the projection, and a writer that read the row before it must not commit a
    /// position derived from the replaced rules. Deliberately clock-free: due-ness is a separate, opt-in concern
    /// (<see cref="WhereProjectionIsDueUsingDatabaseClock{TCronJob}"/>) because the fingerprint sweep must be able to
    /// rebase a definition whether or not its stale projection is due.
    /// </remarks>
    internal static IQueryable<TCronJob> WhereScheduleAdvanceFenceHolds<TCronJob>(
        this IQueryable<TCronJob> q,
        Guid cronJobId,
        DateTime observedReconciledThroughUtc,
        DateTime observedNextDueUtc,
        long expectedScheduleRevision
    )
        where TCronJob : CronJobEntity
    {
        return q.Where(e =>
            e.Id == cronJobId
            && !e.IsPaused
            && e.ScheduleRevision == expectedScheduleRevision
            && e.ReconciledThroughUtc == observedReconciledThroughUtc
            && e.NextDueUtc == observedNextDueUtc
        );
    }

    /// <summary>
    /// Keeps only definitions whose projection the <i>database</i> considers due. <c>DateTime.UtcNow</c> is
    /// provider-translated to server time inside the statement, so a node whose wall clock runs ahead of the database
    /// cannot advance a definition that is not yet due, and one whose clock lags cannot stall one that is.
    /// </summary>
    /// <remarks>
    /// Never evaluate this into a local variable first: that reintroduces the application clock and adds a round trip
    /// whose latency lands the comparison on a different instant than the store's.
    /// </remarks>
    internal static IQueryable<TCronJob> WhereProjectionIsDueUsingDatabaseClock<TCronJob>(this IQueryable<TCronJob> q)
        where TCronJob : CronJobEntity
    {
        return q.Where(e => e.NextDueUtc <= DateTime.UtcNow);
    }

    /// <summary>
    /// Timed-descendant claim gate: keeps a timed chain descendant (<c>ParentId != null</c> AND
    /// <c>ExecutionTime != null</c>) with a parent-terminal-gated <c>RunCondition</c> out of the claim until its parent
    /// reached the MATCHING terminal state. Rows with no parent, no execution time, or a non-gated run condition
    /// (<c>InProgress</c> / <see langword="null"/>) pass untouched. The parent lookup is a correlated subquery over
    /// <paramref name="allJobs"/> (the same <c>DbSet</c>), so EF evaluates the whole predicate inside the atomic claim
    /// on the database — never a pre-evaluated local. Mirrors the in-memory <c>_ParentGateAllowsClaim</c> and the
    /// native-SQL <c>EXISTS</c> gate; the three must stay in lockstep.
    /// </summary>
    internal static IQueryable<TTimeJob> WhereClaimableUnderParentTerminalGate<TTimeJob>(
        this IQueryable<TTimeJob> q,
        IQueryable<TTimeJob> allJobs
    )
        where TTimeJob : TimeJobEntity<TTimeJob>
    {
        return q.Where(e =>
            e.ParentId == null
            || e.ExecutionTime == null
            || (
                e.RunCondition != RunCondition.OnSuccess
                && e.RunCondition != RunCondition.OnFailure
                && e.RunCondition != RunCondition.OnCancelled
                && e.RunCondition != RunCondition.OnFailureOrCancelled
                && e.RunCondition != RunCondition.OnAnyCompletedStatus
            )
            || allJobs.Any(parent =>
                parent.Id == e.ParentId
                && (
                    (
                        e.RunCondition == RunCondition.OnSuccess
                        && (parent.Status == JobStatus.Succeeded || parent.Status == JobStatus.DueDone)
                    )
                    || (e.RunCondition == RunCondition.OnFailure && parent.Status == JobStatus.Failed)
                    || (e.RunCondition == RunCondition.OnCancelled && parent.Status == JobStatus.Cancelled)
                    || (
                        e.RunCondition == RunCondition.OnFailureOrCancelled
                        && (parent.Status == JobStatus.Failed || parent.Status == JobStatus.Cancelled)
                    )
                    || (
                        e.RunCondition == RunCondition.OnAnyCompletedStatus
                        && (
                            parent.Status == JobStatus.Succeeded
                            || parent.Status == JobStatus.DueDone
                            || parent.Status == JobStatus.Failed
                            || parent.Status == JobStatus.Cancelled
                        )
                    )
                )
            )
        );
    }

    /// <summary>
    /// Reconcile helper: keeps only the rows whose parent has reached any terminal state (correlated subquery
    /// over <paramref name="allJobs"/>). Combined with a base filter to idle timed gated children, this yields the
    /// children whose parent has settled and therefore need release-or-skip reconciliation.
    /// </summary>
    internal static IQueryable<TTimeJob> WhereParentIsTerminal<TTimeJob>(
        this IQueryable<TTimeJob> q,
        IQueryable<TTimeJob> allJobs
    )
        where TTimeJob : TimeJobEntity<TTimeJob>
    {
        return q.Where(e =>
            allJobs.Any(parent =>
                parent.Id == e.ParentId
                && (
                    parent.Status == JobStatus.Succeeded
                    || parent.Status == JobStatus.DueDone
                    || parent.Status == JobStatus.Failed
                    || parent.Status == JobStatus.Cancelled
                    || parent.Status == JobStatus.Skipped
                )
            )
        );
    }

    /// <summary>
    /// Bounded-sweep helper: keeps only the rows the skip-only safety net actually mutates — a gated
    /// timed child whose parent reached a terminal state that does NOT satisfy the child's <c>RunCondition</c>. This
    /// is the exact negation of <see cref="WhereClaimableUnderParentTerminalGate{TTimeJob}"/>'s match arm intersected
    /// with "parent is terminal", so the poll-time sweep can bound its selection to the mismatched set — a page full
    /// of matching (release-side) children, which this skip path never touches, can never starve it. Assumes the
    /// caller already filtered to gated timed idle candidates (the escape arms — null parent / null time / non-gated
    /// condition — are excluded upstream), matching the <see cref="WhereParentIsTerminal{TTimeJob}"/> precondition.
    /// A <c>Skipped</c> parent is terminal and matches no run condition, so its children fall here and are skipped.
    /// </summary>
    internal static IQueryable<TTimeJob> WhereParentTerminalRunConditionMismatched<TTimeJob>(
        this IQueryable<TTimeJob> q,
        IQueryable<TTimeJob> allJobs
    )
        where TTimeJob : TimeJobEntity<TTimeJob>
    {
        return q.Where(e =>
            allJobs.Any(parent =>
                parent.Id == e.ParentId
                && (
                    parent.Status == JobStatus.Succeeded
                    || parent.Status == JobStatus.DueDone
                    || parent.Status == JobStatus.Failed
                    || parent.Status == JobStatus.Cancelled
                    || parent.Status == JobStatus.Skipped
                )
                && !(
                    (
                        e.RunCondition == RunCondition.OnSuccess
                        && (parent.Status == JobStatus.Succeeded || parent.Status == JobStatus.DueDone)
                    )
                    || (e.RunCondition == RunCondition.OnFailure && parent.Status == JobStatus.Failed)
                    || (e.RunCondition == RunCondition.OnCancelled && parent.Status == JobStatus.Cancelled)
                    || (
                        e.RunCondition == RunCondition.OnFailureOrCancelled
                        && (parent.Status == JobStatus.Failed || parent.Status == JobStatus.Cancelled)
                    )
                    || (
                        e.RunCondition == RunCondition.OnAnyCompletedStatus
                        && (
                            parent.Status == JobStatus.Succeeded
                            || parent.Status == JobStatus.DueDone
                            || parent.Status == JobStatus.Failed
                            || parent.Status == JobStatus.Cancelled
                        )
                    )
                )
            )
        );
    }

    /// <summary>
    /// Selects the non-terminal rows whose active lease <paramref name="owner"/> holds, for dead-node reclaim and the
    /// owner's own settling writes. Unlike the acquire predicate it drops the no-live-lease arm: a survivor reacting to
    /// a dead incarnation reclaims only that incarnation's rows, never idle rows nobody holds nor a fast restart's
    /// freshly granted rows. Terminal rows are excluded.
    /// </summary>
    public static IQueryable<TTimeJob> WhereOwnedBy<TTimeJob>(this IQueryable<TTimeJob> q, string owner)
        where TTimeJob : TimeJobEntity<TTimeJob>
    {
        return q.Where(e =>
            (e.Status == JobStatus.Idle || e.Status == JobStatus.Queued || e.Status == JobStatus.InProgress)
            && e.Lease.State == RowLeaseState.Active
            && e.Lease.Holder == owner
        );
    }

    /// <inheritdoc cref="WhereOwnedBy{TTimeJob}(System.Linq.IQueryable{TTimeJob},string)"/>
    public static IQueryable<CronJobOccurrenceEntity<TCronJob>> WhereOwnedBy<TCronJob>(
        this IQueryable<CronJobOccurrenceEntity<TCronJob>> q,
        string owner
    )
        where TCronJob : CronJobEntity
    {
        return q.Where(e =>
            (e.Status == JobStatus.Idle || e.Status == JobStatus.Queued || e.Status == JobStatus.InProgress)
            && e.Lease.State == RowLeaseState.Active
            && e.Lease.Holder == owner
        );
    }

    /// <summary>
    /// Selects the rows <paramref name="owner"/> is running: <c>InProgress</c> under an active lease that owner holds.
    /// A row a sweep already reclaimed, terminalized, or handed to another owner matches nothing.
    /// </summary>
    internal static IQueryable<TTimeJob> WhereRunningBy<TTimeJob>(this IQueryable<TTimeJob> q, string owner)
        where TTimeJob : TimeJobEntity<TTimeJob>
    {
        return q.Where(e =>
            e.Status == JobStatus.InProgress && e.Lease.State == RowLeaseState.Active && e.Lease.Holder == owner
        );
    }

    /// <inheritdoc cref="WhereRunningBy{TTimeJob}(System.Linq.IQueryable{TTimeJob},string)"/>
    internal static IQueryable<CronJobOccurrenceEntity<TCronJob>> WhereRunningBy<TCronJob>(
        this IQueryable<CronJobOccurrenceEntity<TCronJob>> q,
        string owner
    )
        where TCronJob : CronJobEntity
    {
        return q.Where(e =>
            e.Status == JobStatus.InProgress && e.Lease.State == RowLeaseState.Active && e.Lease.Holder == owner
        );
    }

    /// <summary>
    /// Selects the rows whose attempt is still active but whose lease lapsed by the database clock: the attempts a
    /// sweep may end.
    /// </summary>
    internal static IQueryable<TTimeJob> WhereLeaseExpiredUsingDatabaseClock<TTimeJob>(this IQueryable<TTimeJob> q)
        where TTimeJob : TimeJobEntity<TTimeJob>
    {
        return q.Where(
            JobsLeaseExpressions.Expand<TTimeJob>(e =>
                e.Lease.State == RowLeaseState.Active && !JobsLeaseExpressions.IsLive(e.Lease)
            )
        );
    }

    /// <inheritdoc cref="WhereLeaseExpiredUsingDatabaseClock{TTimeJob}(IQueryable{TTimeJob})"/>
    internal static IQueryable<CronJobOccurrenceEntity<TCronJob>> WhereLeaseExpiredUsingDatabaseClock<TCronJob>(
        this IQueryable<CronJobOccurrenceEntity<TCronJob>> q
    )
        where TCronJob : CronJobEntity
    {
        return q.Where(
            JobsLeaseExpressions.Expand<CronJobOccurrenceEntity<TCronJob>>(e =>
                e.Lease.State == RowLeaseState.Active && !JobsLeaseExpressions.IsLive(e.Lease)
            )
        );
    }

    /// <summary>
    /// Selects the rows a release may return to <c>Idle</c>: <c>Queued</c> rows whose active lease this owner holds,
    /// claimed but not started. Never matches <c>InProgress</c> work, foreign or unheld rows, or <c>Idle</c> rows:
    /// chain descendants sit <c>Idle</c> under leases the root's owner holds while the root runs, so a release
    /// (including the release-everything empty form) ending those leases would break the running chain's
    /// continuations. A released root's still-leased descendants self-heal: the next root claim grants every idle
    /// descendant again.
    /// </summary>
    public static IQueryable<TTimeJob> WhereReleasableBy<TTimeJob>(this IQueryable<TTimeJob> q, string owner)
        where TTimeJob : TimeJobEntity<TTimeJob>
    {
        return q.Where(e =>
            e.Status == JobStatus.Queued && e.Lease.State == RowLeaseState.Active && e.Lease.Holder == owner
        );
    }

    /// <inheritdoc cref="WhereReleasableBy{TTimeJob}(System.Linq.IQueryable{TTimeJob},string)"/>
    public static IQueryable<CronJobOccurrenceEntity<TCronJob>> WhereReleasableBy<TCronJob>(
        this IQueryable<CronJobOccurrenceEntity<TCronJob>> q,
        string owner
    )
        where TCronJob : CronJobEntity
    {
        return q.Where(e =>
            e.Status == JobStatus.Queued && e.Lease.State == RowLeaseState.Active && e.Lease.Holder == owner
        );
    }
}
