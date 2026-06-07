using System.Linq.Expressions;
using Headless.Jobs.Entities;
using Headless.Jobs.Enums;

namespace Headless.Jobs.Infrastructure;

public static class JobsQueryExtensions
{
    public static IQueryable<TTimeJob> WhereCanAcquire<TTimeJob>(this IQueryable<TTimeJob> q, string lockHolder)
        where TTimeJob : TimeJobEntity<TTimeJob>
    {
        Expression<Func<TTimeJob, bool>> pred = e =>
            ((e.Status == JobStatus.Idle || e.Status == JobStatus.Queued) && e.LockHolder == lockHolder)
            || ((e.Status == JobStatus.Idle || e.Status == JobStatus.Queued) && e.LockedAt == null);

        return q.Where(pred);
    }

    public static IQueryable<CronJobOccurrenceEntity<TCronJob>> WhereCanAcquire<TCronJob>(
        this IQueryable<CronJobOccurrenceEntity<TCronJob>> q,
        string lockHolder
    )
        where TCronJob : CronJobEntity
    {
        return q.Where(e =>
            ((e.Status == JobStatus.Idle || e.Status == JobStatus.Queued) && e.LockHolder == lockHolder)
            || ((e.Status == JobStatus.Idle || e.Status == JobStatus.Queued) && e.LockedAt == null)
        );
    }

    /// <summary>
    /// Selects the non-terminal rows owned by <paramref name="owner"/> for dead-node reclaim. Unlike
    /// <c>WhereCanAcquire</c> this drops the loose <c>LockedAt == null</c> arm (KTD5/R4): a survivor reacting
    /// to a dead incarnation reclaims only that incarnation's rows — never unowned-but-idle rows nor a
    /// fast-restart's freshly-stamped rows. The terminal-state guard is preserved (terminal rows excluded).
    /// </summary>
    public static IQueryable<TTimeJob> WhereOwnedBy<TTimeJob>(this IQueryable<TTimeJob> q, string owner)
        where TTimeJob : TimeJobEntity<TTimeJob>
    {
        return q.Where(e =>
            (e.Status == JobStatus.Idle || e.Status == JobStatus.Queued || e.Status == JobStatus.InProgress)
            && e.LockHolder == owner
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
            && e.LockHolder == owner
        );
    }
}
