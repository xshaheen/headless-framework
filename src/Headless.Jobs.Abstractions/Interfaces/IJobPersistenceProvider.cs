// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Linq.Expressions;
using Headless.Jobs.Entities;
using Headless.Jobs.Models;

namespace Headless.Jobs.Interfaces;

/// <summary>
/// Operational-store SPI for the Jobs scheduler: the durable persistence contract a backend provider (for
/// example the Entity Framework Core store, or the built-in in-memory provider) implements to queue, claim,
/// lease, renew, and terminalize time jobs and cron occurrences. Applications do not call this directly — they
/// schedule work through <c>ITimeJobManager</c> / <c>ICronJobManager</c>, and the scheduler drives this
/// provider. Implementations own the atomicity and ownership fencing described on each member.
/// </summary>
/// <typeparam name="TTimeJob">The application's concrete time job entity type.</typeparam>
/// <typeparam name="TCronJob">The application's concrete cron job entity type.</typeparam>
[PublicAPI]
public interface IJobPersistenceProvider<TTimeJob, TCronJob>
    where TTimeJob : TimeJobEntity<TTimeJob>, new()
    where TCronJob : CronJobEntity, new()
{
    #region Time_Ticker_Core_Methods
    IAsyncEnumerable<TimeJobEntity> QueueTimeJobsAsync(
        TimeJobEntity[] timeJobs,
        CancellationToken cancellationToken = default
    );
    IAsyncEnumerable<TimeJobEntity> QueueTimedOutTimeJobsAsync(CancellationToken cancellationToken = default);
    Task ReleaseAcquiredTimeJobsAsync(Guid[] timeJobIds, CancellationToken cancellationToken = default);
    Task<TimeJobEntity[]> GetEarliestTimeJobsAsync(CancellationToken cancellationToken = default);
    Task<int> UpdateTimeJobAsync(JobExecutionState functionContext, CancellationToken cancellationToken = default);
    Task<byte[]> GetTimeJobRequestAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>
    /// Atomically requests cooperative cancellation by time-job ID. Idle jobs become terminal Cancelled immediately;
    /// Queued and InProgress jobs retain their status and set <c>CancelRequested</c>. Duplicate, terminal, and unknown
    /// requests return <see langword="false"/> without changing audit state.
    /// </summary>
    Task<bool> RequestTimeJobCancellationAsync(Guid jobId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Reads durable cancellation through the provider's current ownership fence. Returns <see langword="true"/> or
    /// <see langword="false"/> only while this node still owns an InProgress row; <see langword="null"/> means the row
    /// is absent, reclaimed, terminal, or owned by another node.
    /// </summary>
    Task<bool?> IsTimeJobCancellationRequestedAsync(Guid jobId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Applies <paramref name="functionContext"/> to still-owned time jobs and returns the IDs that were actually
    /// stamped. Callers must execute only the returned IDs.
    /// </summary>
    Task<Guid[]> UpdateTimeJobsWithUnifiedContextAsync(
        Guid[] timeJobIds,
        JobExecutionState functionContext,
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
    Task<int> RenewTimeJobLeaseAsync(Guid jobId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Reclaims time jobs stuck <c>InProgress</c> whose lease lapsed (<c>LockedUntil &lt;= now</c>), independent of
    /// node death (#316/U3 — the gap-closer). Applies the same per-<c>OnNodeDeath</c> transitions as the dead-node
    /// sweep: <c>Retry</c> → released to <c>Idle</c> (re-claimable), <c>MarkFailed</c> → <c>Failed</c>, <c>Skip</c> →
    /// <c>Skipped</c>. A healthy renewing job keeps a future lease and is never matched. Returns the affected count.
    /// </summary>
    Task<int> ReclaimStalledTimeJobsAsync(CancellationToken cancellationToken = default);
    #endregion

    #region Cron_Ticker_Core_Methods
    Task MigrateDefinedCronJobsAsync(
        (string Function, string Expression)[] cronJobs,
        CancellationToken cancellationToken = default
    );
    Task<CronJobEntity[]> GetAllCronJobExpressionsAsync(CancellationToken cancellationToken = default);
    Task<int> ReleaseDeadNodeTimeJobResourcesAsync(
        string instanceIdentifier,
        CancellationToken cancellationToken = default
    );
    #endregion

    #region Cron_TickerOccurrence_Core_Methods
    Task<CronJobOccurrenceEntity<TCronJob>> GetEarliestAvailableCronOccurrenceAsync(
        Guid[] ids,
        CancellationToken cancellationToken = default
    );
    IAsyncEnumerable<CronJobOccurrenceEntity<TCronJob>> QueueCronJobOccurrencesAsync(
        (DateTime Key, JobManagerDispatchContext[] Items) cronJobOccurrences,
        CancellationToken cancellationToken = default
    );
    IAsyncEnumerable<CronJobOccurrenceEntity<TCronJob>> QueueTimedOutCronJobOccurrencesAsync(
        CancellationToken cancellationToken = default
    );

    // Returns the affected-row count: 0 when the #5 completion fence excluded the row (foreign owner or terminal
    // status), 1 when the completion was applied — mirroring UpdateTimeJobAsync so the cron fence is observable/testable.
    Task<int> UpdateCronJobOccurrenceAsync(
        JobExecutionState functionContext,
        CancellationToken cancellationToken = default
    );
    Task ReleaseAcquiredCronJobOccurrencesAsync(Guid[] occurrenceIds, CancellationToken cancellationToken = default);
    Task<byte[]> GetCronJobOccurrenceRequestAsync(Guid jobId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Applies <paramref name="functionContext"/> to still-owned cron occurrences and returns the IDs that were
    /// actually stamped. Callers must execute only the returned IDs.
    /// </summary>
    Task<Guid[]> UpdateCronJobOccurrencesWithUnifiedContextAsync(
        Guid[] timeJobIds,
        JobExecutionState functionContext,
        CancellationToken cancellationToken = default
    );
    Task<int> ReleaseDeadNodeOccurrenceResourcesAsync(
        string instanceIdentifier,
        CancellationToken cancellationToken = default
    );

    /// <summary>
    /// Slides the running cron occurrence's lease forward (<c>LockedUntil = now + LeaseDuration</c>), fenced on
    /// current ownership + non-terminal status (#316/KTD3). Returns <c>1</c> when renewed, <c>0</c> when the
    /// lease was lost — the caller treats <c>0</c> as cancel-on-loss — or a <b>negative</b> value when coordination
    /// membership is not currently established (#461), treated as "skip this renewal tick", not loss.
    /// </summary>
    Task<int> RenewCronJobOccurrenceLeaseAsync(Guid occurrenceId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Reclaims cron occurrences stuck <c>InProgress</c> whose lease lapsed (#316/U3) — the cron mirror of
    /// <see cref="ReclaimStalledTimeJobsAsync"/>, applying the same per-<c>OnNodeDeath</c> transitions. Returns the
    /// affected count.
    /// </summary>
    Task<int> ReclaimStalledCronJobOccurrencesAsync(CancellationToken cancellationToken = default);
    #endregion

    #region Time_Ticker_Shared_Methods
    Task<TTimeJob?> GetTimeJobByIdAsync(Guid id, CancellationToken cancellationToken = default);

    Task<TTimeJob[]> GetTimeJobsAsync(
        Expression<Func<TTimeJob, bool>>? predicate,
        CancellationToken cancellationToken = default
    );
    Task<PaginationResult<TTimeJob>> GetTimeJobsPaginatedAsync(
        Expression<Func<TTimeJob, bool>>? predicate,
        int pageNumber,
        int pageSize,
        CancellationToken cancellationToken = default
    );
    Task<int> AddTimeJobsAsync(TTimeJob[] jobs, CancellationToken cancellationToken = default);
    Task<int> UpdateTimeJobsAsync(TTimeJob[] jobs, CancellationToken cancellationToken = default);
    Task<int> RemoveTimeJobsAsync(Guid[] jobIds, CancellationToken cancellationToken = default);
    #endregion

    #region Cron_Ticker_Shared_Methods
    Task<TCronJob?> GetCronJobByIdAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>
    /// Atomically pauses a cron definition and skips its pending Idle or Queued occurrences. InProgress work is
    /// preserved. Returns the updated definition, or <see langword="null"/> when the definition is absent or already
    /// paused.
    /// </summary>
    Task<TCronJob?> PauseCronJobAsync(
        Guid cronJobId,
        DateTime operationTimeUtc,
        CancellationToken cancellationToken = default
    );

    /// <summary>
    /// Atomically resumes a paused definition and inserts its single replacement occurrence. The schedule revision
    /// fences stale callers. Returns the updated definition, or <see langword="null"/> when the transition loses the
    /// fence.
    /// </summary>
    Task<TCronJob?> ResumeCronJobAsync(
        Guid cronJobId,
        long expectedScheduleRevision,
        CronJobOccurrenceEntity<TCronJob> nextOccurrence,
        DateTime operationTimeUtc,
        CancellationToken cancellationToken = default
    );

    /// <summary>
    /// Atomically applies a definition batch. Schedule-changing edits retire pending occurrences and insert their
    /// replacement occurrence while metadata-only edits preserve both the schedule revision and pending work.
    /// </summary>
    Task<TCronJob[]?> UpdateCronJobsAtomicallyAsync(
        CronJobAtomicUpdate<TCronJob>[] updates,
        DateTime operationTimeUtc,
        CancellationToken cancellationToken = default
    );

    Task<TCronJob[]> GetCronJobsAsync(
        Expression<Func<TCronJob, bool>>? predicate,
        CancellationToken cancellationToken = default
    );
    Task<PaginationResult<TCronJob>> GetCronJobsPaginatedAsync(
        Expression<Func<TCronJob, bool>>? predicate,
        int pageNumber,
        int pageSize,
        CancellationToken cancellationToken = default
    );
    Task<int> InsertCronJobsAsync(TCronJob[] jobs, CancellationToken cancellationToken = default);
    Task<int> UpdateCronJobsAsync(TCronJob[] cronJob, CancellationToken cancellationToken = default);
    Task<int> RemoveCronJobsAsync(Guid[] cronJobIds, CancellationToken cancellationToken = default);
    #endregion

    #region Cron_TickerOccurrence_Shared_Methods
    Task<CronJobOccurrenceEntity<TCronJob>[]> GetAllCronJobOccurrencesAsync(
        Expression<Func<CronJobOccurrenceEntity<TCronJob>, bool>>? predicate,
        CancellationToken cancellationToken = default
    );

    /// <summary>
    /// Returns storage-reduced status counts for the cron-occurrence dashboard graph, plus zero-count boundary
    /// entries that identify the exact inclusive date range. The default implementation preserves compatibility
    /// for third-party providers by projecting through <see cref="GetAllCronJobOccurrencesAsync"/>; providers
    /// should override it to project distinct dates and aggregate counts in storage.
    /// </summary>
    /// <param name="cronJobId">Identifier of the cron job whose occurrence history is projected.</param>
    /// <param name="today">Current UTC calendar date used to balance the graph around today.</param>
    /// <param name="cancellationToken">Token that can abort the provider query.</param>
    async Task<CronOccurrenceStatusCount[]> GetCronOccurrenceGraphStatusCountsAsync(
        Guid cronJobId,
        DateTime today,
        CancellationToken cancellationToken = default
    )
    {
        var occurrences = await GetAllCronJobOccurrencesAsync(x => x.CronJobId == cronJobId, cancellationToken)
            .ConfigureAwait(false);
        var range = CronOccurrenceGraphRangeSelector.Select(occurrences.Select(x => x.ExecutionTime), today);
        var counts = occurrences
            .Where(x => x.ExecutionTime.Date >= range.StartDate && x.ExecutionTime.Date <= range.EndDate)
            .GroupBy(x => new { x.ExecutionTime.Date, x.Status })
            .Select(group => new CronOccurrenceStatusCount
            {
                Date = group.Key.Date,
                Status = group.Key.Status,
                Count = group.Count(),
            });

        return CronOccurrenceGraphRangeSelector.AddRangeBoundaries(counts, range);
    }

    Task<PaginationResult<CronJobOccurrenceEntity<TCronJob>>> GetAllCronJobOccurrencesPaginatedAsync(
        Expression<Func<CronJobOccurrenceEntity<TCronJob>, bool>> predicate,
        int pageNumber,
        int pageSize,
        CancellationToken cancellationToken = default
    );
    Task<int> InsertCronJobOccurrencesAsync(
        CronJobOccurrenceEntity<TCronJob>[] cronJobOccurrences,
        CancellationToken cancellationToken = default
    );
    Task<int> RemoveCronJobOccurrencesAsync(Guid[] cronJobOccurrences, CancellationToken cancellationToken = default);
    Task<CronJobOccurrenceEntity<TCronJob>[]> AcquireImmediateCronOccurrencesAsync(
        Guid[] occurrenceIds,
        CancellationToken cancellationToken = default
    );
    #endregion
}
