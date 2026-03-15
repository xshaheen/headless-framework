using Headless.Jobs.Entities;
using Headless.Jobs.Models;

namespace Headless.Jobs.Interfaces.Managers;

public interface ITimeJobManager<TTimeTicker>
    where TTimeTicker : TimeJobEntity<TTimeTicker>
{
    Task<JobResult<TTimeTicker>> AddAsync(TTimeTicker entity, CancellationToken cancellationToken = default);
    Task<JobResult<TTimeTicker>> UpdateAsync(TTimeTicker timeTicker, CancellationToken cancellationToken = default);
    Task<JobResult<TTimeTicker>> DeleteAsync(Guid id, CancellationToken cancellationToken = default);

    // Batch operations
    Task<JobResult<List<TTimeTicker>>> AddBatchAsync(
        List<TTimeTicker> entities,
        CancellationToken cancellationToken = default
    );
    Task<JobResult<List<TTimeTicker>>> UpdateBatchAsync(
        List<TTimeTicker> timeTickers,
        CancellationToken cancellationToken = default
    );
    Task<JobResult<TTimeTicker>> DeleteBatchAsync(List<Guid> ids, CancellationToken cancellationToken = default);
}
