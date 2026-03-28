using System.Linq.Expressions;
using Headless.Jobs.Entities;
using Headless.Jobs.Models;

namespace Headless.Jobs.Interfaces;

public interface IJobPersistenceProvider<TTimeJob, TCronJob>
    where TTimeJob : TimeJobEntity<TTimeJob>, new()
    where TCronJob : CronJobEntity, new()
{
    #region Time_Ticker_Core_Methods
    IAsyncEnumerable<TimeJobEntity> QueueTimeJobs(
        TimeJobEntity[] timeJobs,
        CancellationToken cancellationToken = default
    );
    IAsyncEnumerable<TimeJobEntity> QueueTimedOutTimeJobs(CancellationToken cancellationToken = default);
    Task ReleaseAcquiredTimeJobs(Guid[] timeJobIds, CancellationToken cancellationToken = default);
    Task<TimeJobEntity[]> GetEarliestTimeJobs(CancellationToken cancellationToken = default);
    Task<int> UpdateTimeJob(InternalFunctionContext functionContext, CancellationToken cancellationToken = default);
    Task<byte[]> GetTimeJobRequest(Guid id, CancellationToken cancellationToken);
    Task UpdateTimeJobsWithUnifiedContext(
        Guid[] timeJobIds,
        InternalFunctionContext functionContext,
        CancellationToken cancellationToken = default
    );
    Task<TimeJobEntity[]> AcquireImmediateTimeJobsAsync(Guid[] ids, CancellationToken cancellationToken = default);
    #endregion

    #region Cron_Ticker_Core_Methods
    Task MigrateDefinedCronJobs(
        (string Function, string Expression)[] cronJobs,
        CancellationToken cancellationToken = default
    );
    Task<CronJobEntity[]> GetAllCronJobExpressions(CancellationToken cancellationToken);
    Task ReleaseDeadNodeTimeJobResources(string instanceIdentifier, CancellationToken cancellationToken = default);
    #endregion

    #region Cron_TickerOccurrence_Core_Methods
    Task<CronJobOccurrenceEntity<TCronJob>> GetEarliestAvailableCronOccurrence(
        Guid[] ids,
        CancellationToken cancellationToken = default
    );
    IAsyncEnumerable<CronJobOccurrenceEntity<TCronJob>> QueueCronJobOccurrences(
        (DateTime Key, InternalManagerContext[] Items) cronJobOccurrences,
        CancellationToken cancellationToken = default
    );
    IAsyncEnumerable<CronJobOccurrenceEntity<TCronJob>> QueueTimedOutCronJobOccurrences(
        CancellationToken cancellationToken = default
    );
    Task UpdateCronJobOccurrence(
        InternalFunctionContext functionContext,
        CancellationToken cancellationToken = default
    );
    Task ReleaseAcquiredCronJobOccurrences(Guid[] occurrenceIds, CancellationToken cancellationToken = default);
    Task<byte[]> GetCronJobOccurrenceRequest(Guid jobId, CancellationToken cancellationToken = default);
    Task UpdateCronJobOccurrencesWithUnifiedContext(
        Guid[] timeJobIds,
        InternalFunctionContext functionContext,
        CancellationToken cancellationToken = default
    );
    Task ReleaseDeadNodeOccurrenceResources(string instanceIdentifier, CancellationToken cancellationToken = default);
    #endregion

    #region Time_Ticker_Shared_Methods
    Task<TTimeJob?> GetTimeJobById(Guid id, CancellationToken cancellationToken = default);

    Task<TTimeJob[]> GetTimeJobs(
        Expression<Func<TTimeJob, bool>>? predicate,
        CancellationToken cancellationToken = default
    );
    Task<PaginationResult<TTimeJob>> GetTimeJobsPaginated(
        Expression<Func<TTimeJob, bool>>? predicate,
        int pageNumber,
        int pageSize,
        CancellationToken cancellationToken = default
    );
    Task<int> AddTimeJobs(TTimeJob[] jobs, CancellationToken cancellationToken = default);
    Task<int> UpdateTimeJobs(TTimeJob[] jobs, CancellationToken cancellationToken = default);
    Task<int> RemoveTimeJobs(Guid[] jobIds, CancellationToken cancellationToken = default);
    #endregion

    #region Cron_Ticker_Shared_Methods
    Task<TCronJob?> GetCronJobById(Guid id, CancellationToken cancellationToken);
    Task<TCronJob[]> GetCronJobs(Expression<Func<TCronJob, bool>>? predicate, CancellationToken cancellationToken);
    Task<PaginationResult<TCronJob>> GetCronJobsPaginated(
        Expression<Func<TCronJob, bool>>? predicate,
        int pageNumber,
        int pageSize,
        CancellationToken cancellationToken = default
    );
    Task<int> InsertCronJobs(TCronJob[] jobs, CancellationToken cancellationToken);
    Task<int> UpdateCronJobs(TCronJob[] cronJob, CancellationToken cancellationToken);
    Task<int> RemoveCronJobs(Guid[] cronJobIds, CancellationToken cancellationToken);
    #endregion

    #region Cron_TickerOccurrence_Shared_Methods
    Task<CronJobOccurrenceEntity<TCronJob>[]> GetAllCronJobOccurrences(
        Expression<Func<CronJobOccurrenceEntity<TCronJob>, bool>>? predicate,
        CancellationToken cancellationToken = default
    );
    Task<PaginationResult<CronJobOccurrenceEntity<TCronJob>>> GetAllCronJobOccurrencesPaginated(
        Expression<Func<CronJobOccurrenceEntity<TCronJob>, bool>> predicate,
        int pageNumber,
        int pageSize,
        CancellationToken cancellationToken = default
    );
    Task<int> InsertCronJobOccurrences(
        CronJobOccurrenceEntity<TCronJob>[] cronJobOccurrences,
        CancellationToken cancellationToken
    );
    Task<int> RemoveCronJobOccurrences(Guid[] cronJobOccurrences, CancellationToken cancellationToken);
    Task<CronJobOccurrenceEntity<TCronJob>[]> AcquireImmediateCronOccurrencesAsync(
        Guid[] occurrenceIds,
        CancellationToken cancellationToken = default
    );
    #endregion
}
