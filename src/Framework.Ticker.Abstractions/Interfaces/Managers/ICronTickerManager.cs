using Framework.Ticker.Utilities.Entities;
using Framework.Ticker.Utilities.Models;

namespace Framework.Ticker.Utilities.Interfaces.Managers;

public interface ICronTickerManager<TCronTicker>
    where TCronTicker : CronTickerEntity
{
    Task<TickerResult<TCronTicker>> AddAsync(TCronTicker entity, CancellationToken cancellationToken = default);
    Task<TickerResult<TCronTicker>> UpdateAsync(TCronTicker cronTicker, CancellationToken cancellationToken = default);
    Task<TickerResult<TCronTicker>> DeleteAsync(Guid id, CancellationToken cancellationToken = default);

    // Batch operations
    Task<TickerResult<List<TCronTicker>>> AddBatchAsync(
        List<TCronTicker> entities,
        CancellationToken cancellationToken = default
    );
    Task<TickerResult<List<TCronTicker>>> UpdateBatchAsync(
        List<TCronTicker> cronTickers,
        CancellationToken cancellationToken = default
    );
    Task<TickerResult<TCronTicker>> DeleteBatchAsync(List<Guid> ids, CancellationToken cancellationToken = default);
}
