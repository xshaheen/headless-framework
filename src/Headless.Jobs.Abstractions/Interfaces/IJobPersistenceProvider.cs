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

    /// <summary>
    /// Slides the running time job's lease forward (<c>LockedUntil = now + LeaseDuration</c>), fenced on
    /// current ownership + non-terminal status (#316/KTD3). Returns the affected row count: <c>1</c> when the
    /// lease was renewed, <c>0</c> when the lease was lost (reclaimed, owner changed, or terminalized) — the
    /// caller treats <c>0</c> as cancel-on-loss — or a <b>negative</b> value when coordination membership is not
    /// currently established (#461), which the caller treats as "skip this renewal tick", not loss.
    /// </summary>
    Task<int> RenewTimeJobLease(Guid jobId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Reclaims time jobs stuck <c>InProgress</c> whose lease lapsed (<c>LockedUntil &lt;= now</c>), independent of
    /// node death (#316/U3 — the gap-closer). Applies the same per-<c>OnNodeDeath</c> transitions as the dead-node
    /// sweep: <c>Retry</c> → released to <c>Idle</c> (re-claimable), <c>MarkFailed</c> → <c>Failed</c>, <c>Skip</c> →
    /// <c>Skipped</c>. A healthy renewing job keeps a future lease and is never matched. Returns the affected count.
    /// </summary>
    Task<int> ReclaimStalledTimeJobs(CancellationToken cancellationToken = default);
    #endregion

    #region Cron_Ticker_Core_Methods
    Task MigrateDefinedCronJobs(
        (string Function, string Expression)[] cronJobs,
        CancellationToken cancellationToken = default
    );
    Task<CronJobEntity[]> GetAllCronJobExpressions(CancellationToken cancellationToken);
    Task<int> ReleaseDeadNodeTimeJobResources(string instanceIdentifier, CancellationToken cancellationToken = default);
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

    // Returns the affected-row count: 0 when the #5 completion fence excluded the row (foreign owner or terminal
    // status), 1 when the completion was applied — mirroring UpdateTimeJob so the cron fence is observable/testable.
    Task<int> UpdateCronJobOccurrence(
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
    Task<int> ReleaseDeadNodeOccurrenceResources(
        string instanceIdentifier,
        CancellationToken cancellationToken = default
    );

    /// <summary>
    /// Slides the running cron occurrence's lease forward (<c>LockedUntil = now + LeaseDuration</c>), fenced on
    /// current ownership + non-terminal status (#316/KTD3). Returns <c>1</c> when renewed, <c>0</c> when the
    /// lease was lost — the caller treats <c>0</c> as cancel-on-loss — or a <b>negative</b> value when coordination
    /// membership is not currently established (#461), treated as "skip this renewal tick", not loss.
    /// </summary>
    Task<int> RenewCronJobOccurrenceLease(Guid occurrenceId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Reclaims cron occurrences stuck <c>InProgress</c> whose lease lapsed (#316/U3) — the cron mirror of
    /// <see cref="ReclaimStalledTimeJobs"/>, applying the same per-<c>OnNodeDeath</c> transitions. Returns the
    /// affected count.
    /// </summary>
    Task<int> ReclaimStalledCronJobOccurrences(CancellationToken cancellationToken = default);
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
