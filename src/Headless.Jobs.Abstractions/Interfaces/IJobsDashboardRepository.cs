using Headless.Jobs.DashboardDtos;
using Headless.Jobs.Entities;
using Headless.Jobs.Enums;
using Headless.Jobs.Models;

namespace Headless.Jobs.Interfaces;

internal interface IJobsDashboardRepository<TTimeTicker, TCronTicker>
    where TTimeTicker : TimeJobEntity<TTimeTicker>, new()
    where TCronTicker : CronJobEntity, new()
{
    Task<TTimeTicker[]> GetTimeTickersAsync(CancellationToken cancellationToken = default);
    Task<PaginationResult<TTimeTicker>> GetTimeTickersPaginatedAsync(
        int pageNumber,
        int pageSize,
        CancellationToken cancellationToken = default
    );
    Task<IList<Tuple<JobStatus, int>>> GetTimeTickerFullDataAsync(CancellationToken cancellationToken);
    Task<IList<JobGraphData>> GetTimeTickersGraphSpecificDataAsync(
        int pastDays,
        int futureDays,
        CancellationToken cancellationToken
    );
    Task<IList<JobGraphData>> GetCronTickersGraphSpecificDataAsync(
        int pastDays,
        int futureDays,
        CancellationToken cancellationToken
    );
    Task<IList<JobGraphData>> GetCronTickersGraphSpecificDataByIdAsync(
        Guid id,
        int pastDays,
        int futureDays,
        CancellationToken cancellationToken
    );
    Task<IList<Tuple<JobStatus, int>>> GetCronTickerFullDataAsync(CancellationToken cancellationToken);
    Task<CronJobEntity[]> GetCronTickersAsync(CancellationToken cancellationToken = default);
    Task<PaginationResult<CronJobEntity>> GetCronTickersPaginatedAsync(
        int pageNumber,
        int pageSize,
        CancellationToken cancellationToken = default
    );
    Task AddOnDemandCronTickerOccurrenceAsync(Guid id, CancellationToken cancellationToken = default);
    Task<CronJobOccurrenceEntity<TCronTicker>[]> GetCronTickersOccurrencesAsync(
        Guid guid,
        CancellationToken cancellationToken = default
    );
    Task<PaginationResult<CronJobOccurrenceEntity<TCronTicker>>> GetCronTickersOccurrencesPaginatedAsync(
        Guid guid,
        int pageNumber,
        int pageSize,
        CancellationToken cancellationToken = default
    );
    Task<IList<CronOccurrenceJobGraphData>> GetCronTickersOccurrencesGraphDataAsync(
        Guid guid,
        CancellationToken cancellationToken = default
    );
    bool CancelJobById(Guid tickerId);
    Task DeleteCronTickerOccurrenceByIdAsync(Guid id, CancellationToken cancellationToken = default);
    Task<(string, int)> GetJobRequestByIdAsync(
        Guid tickerId,
        JobType tickerType,
        CancellationToken cancellationToken = default
    );
    IEnumerable<(string, (string, string, JobPriority))> GetJobFunctions();
    Task<IList<(int, int)>> GetLastWeekJobStatusesAsync(CancellationToken cancellationToken = default);
    Task<IList<(JobStatus, int)>> GetOverallJobStatusesAsync(CancellationToken cancellationToken = default);
    Task<IList<(string, int)>> GetMachineJobsAsync(CancellationToken cancellationToken = default);
}
