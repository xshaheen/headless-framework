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
        where TTimeTicker : TimeTickerEntity<TTimeTicker>
    {
        Expression<Func<TTimeTicker, bool>> pred = e =>
            ((e.Status == TickerStatus.Idle || e.Status == TickerStatus.Queued) && e.LockHolder == lockHolder)
            || ((e.Status == TickerStatus.Idle || e.Status == TickerStatus.Queued) && e.LockedAt == null);

        return q.Where(pred);
    }

    public static IQueryable<CronTickerOccurrenceEntity<TCronTicker>> WhereCanAcquire<TCronTicker>(
        this IQueryable<CronTickerOccurrenceEntity<TCronTicker>> q,
        string lockHolder
    )
        where TCronTicker : CronTickerEntity
    {
        return q.Where(e =>
            ((e.Status == TickerStatus.Idle || e.Status == TickerStatus.Queued) && e.LockHolder == lockHolder)
            || ((e.Status == TickerStatus.Idle || e.Status == TickerStatus.Queued) && e.LockedAt == null)
        );
    }
}
