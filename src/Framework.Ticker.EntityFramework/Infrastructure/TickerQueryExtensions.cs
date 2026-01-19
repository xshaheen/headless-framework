using System.Linq.Expressions;
using Framework.Ticker.Utilities.Entities;
using Framework.Ticker.Utilities.Enums;

namespace Framework.Ticker.Infrastructure;

public static class TickerQueryExtensions
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
