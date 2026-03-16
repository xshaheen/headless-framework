using Headless.Jobs.DashboardDtos;
using Headless.Jobs.Entities;
using Headless.Jobs.Enums;
using Headless.Jobs.Models;

namespace Headless.Jobs.Interfaces;

internal interface IJobsDashboardRepository<TTimeJob, TCronJob>
    where TTimeJob : TimeJobEntity<TTimeJob>, new()
    where TCronJob : CronJobEntity, new()
{
    Task<TTimeJob[]> GetTimeJobsAsync(CancellationToken cancellationToken = default);
    Task<PaginationResult<TTimeJob>> GetTimeJobsPaginatedAsync(
        int pageNumber,
        int pageSize,
        CancellationToken cancellationToken = default
    );
    Task<IList<Tuple<JobStatus, int>>> GetTimeJobFullDataAsync(CancellationToken cancellationToken);
    Task<IList<JobGraphData>> GetTimeJobsGraphSpecificDataAsync(
        int pastDays,
        int futureDays,
        CancellationToken cancellationToken
    );
    Task<IList<JobGraphData>> GetCronJobsGraphSpecificDataAsync(
        int pastDays,
        int futureDays,
        CancellationToken cancellationToken
    );
    Task<IList<JobGraphData>> GetCronJobsGraphSpecificDataByIdAsync(
        Guid id,
        int pastDays,
        int futureDays,
        CancellationToken cancellationToken
    );
    Task<IList<Tuple<JobStatus, int>>> GetCronJobFullDataAsync(CancellationToken cancellationToken);
    Task<CronJobEntity[]> GetCronJobsAsync(CancellationToken cancellationToken = default);
    Task<PaginationResult<CronJobEntity>> GetCronJobsPaginatedAsync(
        int pageNumber,
        int pageSize,
        CancellationToken cancellationToken = default
    );
    Task AddOnDemandCronJobOccurrenceAsync(Guid id, CancellationToken cancellationToken = default);
    Task<CronJobOccurrenceEntity<TCronJob>[]> GetCronJobsOccurrencesAsync(
        Guid guid,
        CancellationToken cancellationToken = default
    );
    Task<PaginationResult<CronJobOccurrenceEntity<TCronJob>>> GetCronJobsOccurrencesPaginatedAsync(
        Guid guid,
        int pageNumber,
        int pageSize,
        CancellationToken cancellationToken = default
    );
    Task<IList<CronOccurrenceJobGraphData>> GetCronJobsOccurrencesGraphDataAsync(
        Guid guid,
        CancellationToken cancellationToken = default
    );
    bool CancelJobById(Guid jobId);
    Task DeleteCronJobOccurrenceByIdAsync(Guid id, CancellationToken cancellationToken = default);
    Task<(string, int)> GetJobRequestByIdAsync(
        Guid jobId,
        JobType jobType,
        CancellationToken cancellationToken = default
    );
    IEnumerable<(string, (string, string, JobPriority))> GetJobFunctions();
    Task<IList<(int, int)>> GetLastWeekJobStatusesAsync(CancellationToken cancellationToken = default);
    Task<IList<(JobStatus, int)>> GetOverallJobStatusesAsync(CancellationToken cancellationToken = default);
    Task<IList<(string, int)>> GetMachineJobsAsync(CancellationToken cancellationToken = default);
}
