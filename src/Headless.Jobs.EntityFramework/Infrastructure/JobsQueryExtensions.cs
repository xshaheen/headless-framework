using System.Linq.Expressions;
using Headless.Jobs.Entities;
using Headless.Jobs.Enums;

namespace Headless.Jobs.Infrastructure;

public static class JobsQueryExtensions
{
    public static IQueryable<TTimeTicker> WhereCanAcquire<TTimeTicker>(
        this IQueryable<TTimeTicker> q,
        string lockHolder
    )
        where TTimeTicker : TimeJobEntity<TTimeTicker>
    {
        Expression<Func<TTimeTicker, bool>> pred = e =>
            ((e.Status == JobStatus.Idle || e.Status == JobStatus.Queued) && e.LockHolder == lockHolder)
            || ((e.Status == JobStatus.Idle || e.Status == JobStatus.Queued) && e.LockedAt == null);

        return q.Where(pred);
    }

    public static IQueryable<CronJobOccurrenceEntity<TCronTicker>> WhereCanAcquire<TCronTicker>(
        this IQueryable<CronJobOccurrenceEntity<TCronTicker>> q,
        string lockHolder
    )
        where TCronTicker : CronJobEntity
    {
        return q.Where(e =>
            ((e.Status == JobStatus.Idle || e.Status == JobStatus.Queued) && e.LockHolder == lockHolder)
            || ((e.Status == JobStatus.Idle || e.Status == JobStatus.Queued) && e.LockedAt == null)
        );
    }
}
