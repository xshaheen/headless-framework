using Headless.Jobs.Entities;
using Headless.Jobs.Models;

namespace Headless.Jobs.Interfaces.Managers;

public interface ITimeJobManager<TTimeJob>
    where TTimeJob : TimeJobEntity<TTimeJob>
{
    Task<JobResult<TTimeJob>> AddAsync(TTimeJob entity, CancellationToken cancellationToken = default);
    Task<JobResult<TTimeJob>> UpdateAsync(TTimeJob timeJob, CancellationToken cancellationToken = default);
    Task<JobResult<TTimeJob>> DeleteAsync(Guid id, CancellationToken cancellationToken = default);

    // Batch operations
    Task<JobResult<List<TTimeJob>>> AddBatchAsync(
        List<TTimeJob> entities,
        CancellationToken cancellationToken = default
    );
    Task<JobResult<List<TTimeJob>>> UpdateBatchAsync(
        List<TTimeJob> timeJobs,
        CancellationToken cancellationToken = default
    );
    Task<JobResult<TTimeJob>> DeleteBatchAsync(List<Guid> ids, CancellationToken cancellationToken = default);
}
