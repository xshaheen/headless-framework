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
}
