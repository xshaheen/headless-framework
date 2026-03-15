using Headless.Jobs.Entities;
using Headless.Jobs.Models;

namespace Headless.Jobs.Interfaces.Managers;

public interface ICronJobManager<TCronTicker>
    where TCronTicker : CronJobEntity
{
    Task<JobResult<TCronTicker>> AddAsync(TCronTicker entity, CancellationToken cancellationToken = default);
    Task<JobResult<TCronTicker>> UpdateAsync(TCronTicker cronTicker, CancellationToken cancellationToken = default);
    Task<JobResult<TCronTicker>> DeleteAsync(Guid id, CancellationToken cancellationToken = default);

    // Batch operations
    Task<JobResult<List<TCronTicker>>> AddBatchAsync(
        List<TCronTicker> entities,
        CancellationToken cancellationToken = default
    );
    Task<JobResult<List<TCronTicker>>> UpdateBatchAsync(
        List<TCronTicker> cronTickers,
        CancellationToken cancellationToken = default
    );
    Task<JobResult<TCronTicker>> DeleteBatchAsync(List<Guid> ids, CancellationToken cancellationToken = default);
}
