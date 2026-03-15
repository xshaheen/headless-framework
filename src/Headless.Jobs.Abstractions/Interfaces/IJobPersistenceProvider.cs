using System.Linq.Expressions;
using Headless.Jobs.Entities;
using Headless.Jobs.Models;

namespace Headless.Jobs.Interfaces;

public interface IJobPersistenceProvider<TTimeTicker, TCronTicker>
    where TTimeTicker : TimeJobEntity<TTimeTicker>, new()
    where TCronTicker : CronJobEntity, new()
{
    #region Time_Ticker_Core_Methods
    IAsyncEnumerable<TimeJobEntity> QueueTimeTickers(
        TimeJobEntity[] timeTickers,
        CancellationToken cancellationToken = default
    );
    IAsyncEnumerable<TimeJobEntity> QueueTimedOutTimeTickers(CancellationToken cancellationToken = default);
    Task ReleaseAcquiredTimeTickers(Guid[] timeJobIds, CancellationToken cancellationToken = default);
    Task<TimeJobEntity[]> GetEarliestTimeTickers(CancellationToken cancellationToken = default);
    Task<int> UpdateTimeTicker(InternalFunctionContext functionContext, CancellationToken cancellationToken = default);
    Task<byte[]> GetTimeTickerRequest(Guid id, CancellationToken cancellationToken);
    Task UpdateTimeTickersWithUnifiedContext(
        Guid[] timeJobIds,
        InternalFunctionContext functionContext,
        CancellationToken cancellationToken = default
    );
    Task<TimeJobEntity[]> AcquireImmediateTimeTickersAsync(
        Guid[] ids,
        CancellationToken cancellationToken = default
    );
    #endregion

    #region Cron_Ticker_Core_Methods
    Task MigrateDefinedCronTickers(
        (string Function, string Expression)[] cronTickers,
        CancellationToken cancellationToken = default
    );
    Task<CronJobEntity[]> GetAllCronTickerExpressions(CancellationToken cancellationToken);
    Task ReleaseDeadNodeTimeTickerResources(string instanceIdentifier, CancellationToken cancellationToken = default);
    #endregion

    #region Cron_TickerOccurrence_Core_Methods
    Task<CronJobOccurrenceEntity<TCronTicker>> GetEarliestAvailableCronOccurrence(
        Guid[] ids,
        CancellationToken cancellationToken = default
    );
    IAsyncEnumerable<CronJobOccurrenceEntity<TCronTicker>> QueueCronTickerOccurrences(
        (DateTime Key, InternalManagerContext[] Items) cronTickerOccurrences,
        CancellationToken cancellationToken = default
    );
    IAsyncEnumerable<CronJobOccurrenceEntity<TCronTicker>> QueueTimedOutCronTickerOccurrences(
        CancellationToken cancellationToken = default
    );
    Task UpdateCronTickerOccurrence(
        InternalFunctionContext functionContext,
        CancellationToken cancellationToken = default
    );
    Task ReleaseAcquiredCronTickerOccurrences(Guid[] occurrenceIds, CancellationToken cancellationToken = default);
    Task<byte[]> GetCronJobOccurrenceRequest(Guid tickerId, CancellationToken cancellationToken = default);
    Task UpdateCronTickerOccurrencesWithUnifiedContext(
        Guid[] timeJobIds,
        InternalFunctionContext functionContext,
        CancellationToken cancellationToken = default
    );
    Task ReleaseDeadNodeOccurrenceResources(string instanceIdentifier, CancellationToken cancellationToken = default);
    #endregion

    #region Time_Ticker_Shared_Methods
    Task<TTimeTicker?> GetTimeTickerById(Guid id, CancellationToken cancellationToken = default);

    Task<TTimeTicker[]> GetTimeTickers(
        Expression<Func<TTimeTicker, bool>>? predicate,
        CancellationToken cancellationToken = default
    );
    Task<PaginationResult<TTimeTicker>> GetTimeTickersPaginated(
        Expression<Func<TTimeTicker, bool>>? predicate,
        int pageNumber,
        int pageSize,
        CancellationToken cancellationToken = default
    );
    Task<int> AddTimeTickers(TTimeTicker[] tickers, CancellationToken cancellationToken = default);
    Task<int> UpdateTimeTickers(TTimeTicker[] tickers, CancellationToken cancellationToken = default);
    Task<int> RemoveTimeTickers(Guid[] tickerIds, CancellationToken cancellationToken = default);
    #endregion

    #region Cron_Ticker_Shared_Methods
    Task<TCronTicker?> GetCronTickerById(Guid id, CancellationToken cancellationToken);
    Task<TCronTicker[]> GetCronTickers(
        Expression<Func<TCronTicker, bool>>? predicate,
        CancellationToken cancellationToken
    );
    Task<PaginationResult<TCronTicker>> GetCronTickersPaginated(
        Expression<Func<TCronTicker, bool>>? predicate,
        int pageNumber,
        int pageSize,
        CancellationToken cancellationToken = default
    );
    Task<int> InsertCronTickers(TCronTicker[] tickers, CancellationToken cancellationToken);
    Task<int> UpdateCronTickers(TCronTicker[] cronTicker, CancellationToken cancellationToken);
    Task<int> RemoveCronTickers(Guid[] cronJobIds, CancellationToken cancellationToken);
    #endregion

    #region Cron_TickerOccurrence_Shared_Methods
    Task<CronJobOccurrenceEntity<TCronTicker>[]> GetAllCronTickerOccurrences(
        Expression<Func<CronJobOccurrenceEntity<TCronTicker>, bool>>? predicate,
        CancellationToken cancellationToken = default
    );
    Task<PaginationResult<CronJobOccurrenceEntity<TCronTicker>>> GetAllCronTickerOccurrencesPaginated(
        Expression<Func<CronJobOccurrenceEntity<TCronTicker>, bool>> predicate,
        int pageNumber,
        int pageSize,
        CancellationToken cancellationToken = default
    );
    Task<int> InsertCronTickerOccurrences(
        CronJobOccurrenceEntity<TCronTicker>[] cronTickerOccurrences,
        CancellationToken cancellationToken
    );
    Task<int> RemoveCronTickerOccurrences(Guid[] cronTickerOccurrences, CancellationToken cancellationToken);
    Task<CronJobOccurrenceEntity<TCronTicker>[]> AcquireImmediateCronOccurrencesAsync(
        Guid[] occurrenceIds,
        CancellationToken cancellationToken = default
    );
    #endregion
}
