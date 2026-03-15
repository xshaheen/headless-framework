using Headless.Jobs.Entities;
using Headless.Jobs.Models;

namespace Headless.Jobs.Interfaces.Managers;

public interface ICronJobManager<TCronJob>
    where TCronJob : CronJobEntity
{
    Task<JobResult<TCronJob>> AddAsync(TCronJob entity, CancellationToken cancellationToken = default);
    Task<JobResult<TCronJob>> UpdateAsync(TCronJob cronJob, CancellationToken cancellationToken = default);
    Task<JobResult<TCronJob>> DeleteAsync(Guid id, CancellationToken cancellationToken = default);

    // Batch operations
    Task<JobResult<List<TCronJob>>> AddBatchAsync(
        List<TCronJob> entities,
        CancellationToken cancellationToken = default
    );
    Task<JobResult<List<TCronJob>>> UpdateBatchAsync(
        List<TCronJob> cronJobs,
        CancellationToken cancellationToken = default
    );
    Task<JobResult<TCronJob>> DeleteBatchAsync(List<Guid> ids, CancellationToken cancellationToken = default);
}
