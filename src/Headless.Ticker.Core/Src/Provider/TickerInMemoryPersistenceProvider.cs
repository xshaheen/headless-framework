using System.Collections.Concurrent;
using System.Linq.Expressions;
using System.Runtime.CompilerServices;
using Headless.Ticker.Entities;
using Headless.Ticker.Enums;
using Headless.Ticker.Interfaces;
using Headless.Ticker.Models;
using Microsoft.Extensions.DependencyInjection;

namespace Headless.Ticker.Provider;

internal class TickerInMemoryPersistenceProvider<TTimeTicker, TCronTicker>
    : ITickerPersistenceProvider<TTimeTicker, TCronTicker>
    where TTimeTicker : TimeTickerEntity<TTimeTicker>, new()
    where TCronTicker : CronTickerEntity, new()
{
    private static readonly ConcurrentDictionary<Guid, TTimeTicker> _TimeTickers = new(
        new Dictionary<Guid, TTimeTicker>()
    );

    // Index of parent -> child ids for fast hierarchy lookup in memory
    private static readonly ConcurrentDictionary<Guid, ConcurrentDictionary<Guid, byte>> _ChildrenIndex = new(
        new Dictionary<Guid, ConcurrentDictionary<Guid, byte>>()
    );

    private static readonly ConcurrentDictionary<Guid, TCronTicker> _CronTickers = new(
        new Dictionary<Guid, TCronTicker>()
    );

    private static readonly ConcurrentDictionary<Guid, CronTickerOccurrenceEntity<TCronTicker>> _CronOccurrences = new(
        new Dictionary<Guid, CronTickerOccurrenceEntity<TCronTicker>>()
    );

    private readonly ITickerClock _clock;
    private readonly string _lockHolder;

    public TickerInMemoryPersistenceProvider(IServiceProvider serviceProvider)
    {
        _clock = serviceProvider.GetService<ITickerClock>() ?? new TickerSystemClock();
        var optionsBuilder = serviceProvider.GetService<SchedulerOptionsBuilder>();
        _lockHolder = optionsBuilder?.NodeIdentifier ?? Environment.MachineName;
    }

    #region Time Ticker Methods

    public async IAsyncEnumerable<TimeTickerEntity> QueueTimeTickers(
        TimeTickerEntity[] timeTickers,
        [EnumeratorCancellation] CancellationToken cancellationToken = default
    )
    {
        var now = _clock.UtcNow;

        foreach (var timeTicker in timeTickers)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (_TimeTickers.TryGetValue(timeTicker.Id, out var existingTicker))
            {
                // Check if we can update (similar to optimistic concurrency)
                if (existingTicker.UpdatedAt == timeTicker.UpdatedAt)
                {
                    // Update the ticker
                    var updatedTicker = _CloneTicker(existingTicker);
                    updatedTicker.LockHolder = _lockHolder;
                    updatedTicker.LockedAt = now;
                    updatedTicker.UpdatedAt = now;
                    updatedTicker.Status = TickerStatus.Queued;

                    if (_TimeTickers.TryUpdate(timeTicker.Id, updatedTicker, existingTicker))
                    {
                        timeTicker.UpdatedAt = now;
                        timeTicker.LockHolder = _lockHolder;
                        timeTicker.LockedAt = now;
                        timeTicker.Status = TickerStatus.Queued;

                        yield return timeTicker;
                    }
                }
            }
        }

        await Task.CompletedTask;
    }

    public async IAsyncEnumerable<TimeTickerEntity> QueueTimedOutTimeTickers(
        [EnumeratorCancellation] CancellationToken cancellationToken = default
    )
    {
        var now = _clock.UtcNow;
        var fallbackThreshold = now.AddSeconds(-1); // Fallback picks up tasks older than main 1-second window

        // First, get the time tickers that need to be updated (matching EF query)
        // NOTE: we project to the raw ticker here and only build the full
        //       TimeTickerEntity graph after we successfully acquire the lock.
        var timeTickersToUpdate = _TimeTickers
            .Values.Where(x => x.ExecutionTime != null)
            .Where(x => x.Status is TickerStatus.Idle or TickerStatus.Queued)
            .Where(x => x.ExecutionTime <= fallbackThreshold) // Only tasks older than 1 second
            .ToArray();

        foreach (var ticker in timeTickersToUpdate)
        {
            cancellationToken.ThrowIfCancellationRequested();

            // Now update the actual ticker in storage
            if (_TimeTickers.TryGetValue(ticker.Id, out var existingTicker))
            {
                // Check if we can update (matching EF's Where condition)
                if (existingTicker.UpdatedAt <= ticker.UpdatedAt)
                {
                    var updatedTicker = _CloneTicker(existingTicker);
                    updatedTicker.LockHolder = _lockHolder;
                    updatedTicker.LockedAt = now;
                    updatedTicker.UpdatedAt = now;
                    updatedTicker.Status = TickerStatus.InProgress;

                    if (_TimeTickers.TryUpdate(ticker.Id, updatedTicker, existingTicker))
                    {
                        // Only build the full hierarchy for successfully acquired tickers
                        yield return _ForQueueTimeTickers(ticker);
                    }
                }
            }
        }

        await Task.CompletedTask;
    }

    public Task ReleaseAcquiredTimeTickers(Guid[] timeTickerIds, CancellationToken cancellationToken = default)
    {
        var now = _clock.UtcNow;
        var idsToRelease = timeTickerIds.Length == 0 ? _TimeTickers.Keys.ToArray() : timeTickerIds;

        foreach (var id in idsToRelease)
        {
            if (_TimeTickers.TryGetValue(id, out var ticker))
            {
                // Check if we can release (similar to WhereCanAcquire)
                if (_CanAcquire(ticker))
                {
                    var updatedTicker = _CloneTicker(ticker);
                    updatedTicker.LockHolder = null;
                    updatedTicker.LockedAt = null;
                    updatedTicker.Status = TickerStatus.Idle;
                    updatedTicker.UpdatedAt = now;

                    _TimeTickers.TryUpdate(id, updatedTicker, ticker);
                }
            }
        }

        return Task.CompletedTask;
    }

    public Task<TimeTickerEntity[]> GetEarliestTimeTickers(CancellationToken cancellationToken = default)
    {
        var now = _clock.UtcNow;
        var oneSecondAgo = now.AddSeconds(-1);

        // Base query: same filter as EF provider, but over the snapshot
        var baseQuery = _TimeTickers
            .Values.Where(x => x.ExecutionTime != null)
            .Where(_CanAcquire)
            .Where(x => x.ExecutionTime >= oneSecondAgo)
            .ToArray();

        // Get minimum execution time
        var minExecutionTime = baseQuery.OrderBy(x => x.ExecutionTime).Select(x => x.ExecutionTime).FirstOrDefault();

        if (minExecutionTime == null)
        {
            return Task.FromResult(Array.Empty<TimeTickerEntity>());
        }

        // Round the minimum execution time down to its second
        var minSecond = new DateTime(
            minExecutionTime.Value.Year,
            minExecutionTime.Value.Month,
            minExecutionTime.Value.Day,
            minExecutionTime.Value.Hour,
            minExecutionTime.Value.Minute,
            minExecutionTime.Value.Second,
            DateTimeKind.Utc
        );

        var maxExecutionTime = minSecond.AddSeconds(1);

        // Fetch all tickers within that complete second and map using the children lookup
        var result = baseQuery
            .Where(x => x.ExecutionTime >= minSecond && x.ExecutionTime < maxExecutionTime)
            .OrderBy(x => x.ExecutionTime)
            .Select(_ForQueueTimeTickers)
            .ToArray();

        return Task.FromResult(result);
    }

    public Task<int> UpdateTimeTicker(
        InternalFunctionContext functionContext,
        CancellationToken cancellationToken = default
    )
    {
        if (_TimeTickers.TryGetValue(functionContext.TickerId, out var ticker))
        {
            var updatedTicker = _CloneTicker(ticker);
            _ApplyFunctionContextToTicker(updatedTicker, functionContext);

            if (_TimeTickers.TryUpdate(functionContext.TickerId, updatedTicker, ticker))
            {
                return Task.FromResult(1);
            }
        }

        return Task.FromResult(0);
    }

    public Task<byte[]> GetTimeTickerRequest(Guid id, CancellationToken cancellationToken)
    {
        if (_TimeTickers.TryGetValue(id, out var ticker))
        {
            return Task.FromResult(ticker.Request ?? Array.Empty<byte>());
        }

        return Task.FromResult(Array.Empty<byte>());
    }

    public Task UpdateTimeTickersWithUnifiedContext(
        Guid[] timeTickerIds,
        InternalFunctionContext functionContext,
        CancellationToken cancellationToken = default
    )
    {
        foreach (var id in timeTickerIds)
        {
            if (_TimeTickers.TryGetValue(id, out var ticker))
            {
                var updatedTicker = _CloneTicker(ticker);
                _ApplyFunctionContextToTicker(updatedTicker, functionContext);
                _TimeTickers.TryUpdate(id, updatedTicker, ticker);
            }
        }

        return Task.CompletedTask;
    }

    public Task<TimeTickerEntity[]> AcquireImmediateTimeTickersAsync(
        Guid[] ids,
        CancellationToken cancellationToken = default
    )
    {
        if (ids == null || ids.Length == 0)
        {
            return Task.FromResult(Array.Empty<TimeTickerEntity>());
        }

        var now = _clock.UtcNow;
        var acquired = new List<TimeTickerEntity>();

        foreach (var id in ids)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (!_TimeTickers.TryGetValue(id, out var ticker))
            {
                continue;
            }

            if (!_CanAcquire(ticker))
            {
                continue;
            }

            var updatedTicker = _CloneTicker(ticker);
            updatedTicker.LockHolder = _lockHolder;
            updatedTicker.LockedAt = now;
            updatedTicker.Status = TickerStatus.InProgress;
            updatedTicker.UpdatedAt = now;

            if (_TimeTickers.TryUpdate(id, updatedTicker, ticker))
            {
                acquired.Add(_ForQueueTimeTickers(updatedTicker));
            }
        }

        return Task.FromResult(acquired.ToArray());
    }

    public Task<TTimeTicker?> GetTimeTickerById(Guid id, CancellationToken cancellationToken = default)
    {
        if (_TimeTickers.TryGetValue(id, out var ticker))
        {
            var result = _BuildTickerHierarchy(ticker);
            return Task.FromResult<TTimeTicker?>(result);
        }

        return Task.FromResult<TTimeTicker?>(null);
    }

    public Task<TTimeTicker[]> GetTimeTickers(
        Expression<Func<TTimeTicker, bool>>? predicate,
        CancellationToken cancellationToken = default
    )
    {
        var compiledPredicate = predicate?.Compile();
        var query = _TimeTickers.Values.AsEnumerable();

        if (compiledPredicate != null)
        {
            query = query.Where(compiledPredicate);
        }

        // Match EF Core - only return root items (ParentId == null) with nested children
        var results = query
            .Where(x => x.ParentId == null) // Only root items, matching EF Core
            .OrderByDescending(x => x.ExecutionTime) // Match EF Core's OrderByDescending(x => x.ExecutionTime)
            .Select(_BuildTickerHierarchy)
            .ToArray();

        return Task.FromResult(results);
    }

    public Task<PaginationResult<TTimeTicker>> GetTimeTickersPaginated(
        Expression<Func<TTimeTicker, bool>>? predicate,
        int pageNumber,
        int pageSize,
        CancellationToken cancellationToken = default
    )
    {
        var compiledPredicate = predicate?.Compile();
        var query = _TimeTickers.Values.AsEnumerable();

        if (compiledPredicate != null)
        {
            query = query.Where(compiledPredicate);
        }

        // Match EF Core - only count and paginate root items
        query = query.Where(x => x.ParentId == null);

        var totalCount = query.Count();

        var items = query
            .OrderByDescending(x => x.ExecutionTime) // Match EF Core's OrderByDescending(x => x.ExecutionTime)
            .Skip((pageNumber - 1) * pageSize)
            .Take(pageSize)
            .Select(_BuildTickerHierarchy)
            .ToArray();

        return Task.FromResult(
            new PaginationResult<TTimeTicker>
            {
                Items = items,
                TotalCount = totalCount,
                PageNumber = pageNumber,
                PageSize = pageSize,
            }
        );
    }

    public Task<int> AddTimeTickers(TTimeTicker[] tickers, CancellationToken cancellationToken = default)
    {
        var count = 0;
        foreach (var ticker in tickers)
        {
            count += _AddTickerWithChildren(ticker);
        }

        return Task.FromResult(count);
    }

    private static int _AddTickerWithChildren(TTimeTicker ticker, Guid? parentId = null)
    {
        var count = 0;

        // Set the parent ID if this is a child
        if (parentId.HasValue)
        {
            ticker.ParentId = parentId.Value;
        }

        // Add the ticker itself
        if (_TimeTickers.TryAdd(ticker.Id, ticker))
        {
            // Maintain children index
            if (ticker.ParentId.HasValue)
            {
                _AddChildIndex(ticker.ParentId.Value, ticker.Id);
            }

            count++;

            // Recursively add all children
            if (ticker.Children != null && ticker.Children.Count > 0)
            {
                foreach (var child in ticker.Children)
                {
                    // Cast to TTimeTicker since Children is ICollection<TTimeTicker>
                    if (child is TTimeTicker childTicker)
                    {
                        count += _AddTickerWithChildren(childTicker, ticker.Id);
                    }
                }
            }
        }

        return count;
    }

    public Task<int> UpdateTimeTickers(TTimeTicker[] tickers, CancellationToken cancellationToken = default)
    {
        var count = 0;
        foreach (var ticker in tickers)
        {
            count += _UpdateTickerWithChildren(ticker);
        }

        return Task.FromResult(count);
    }

    private static int _UpdateTickerWithChildren(TTimeTicker ticker, Guid? parentId = null)
    {
        var count = 0;

        // Set the parent ID if this is a child
        if (parentId.HasValue)
        {
            ticker.ParentId = parentId.Value;
        }

        // Update the ticker itself
        if (_TimeTickers.TryGetValue(ticker.Id, out var existing))
        {
            if (_TimeTickers.TryUpdate(ticker.Id, ticker, existing))
            {
                // Maintain children index for parent changes
                if (existing.ParentId != ticker.ParentId)
                {
                    if (existing.ParentId.HasValue)
                    {
                        _RemoveChildIndex(existing.ParentId.Value, ticker.Id);
                    }

                    if (ticker.ParentId.HasValue)
                    {
                        _AddChildIndex(ticker.ParentId.Value, ticker.Id);
                    }
                }

                count++;

                // Recursively update all children
                if (ticker.Children != null && ticker.Children.Count > 0)
                {
                    foreach (var child in ticker.Children)
                    {
                        // Cast to TTimeTicker since Children is ICollection<TTimeTicker>
                        if (child is TTimeTicker childTicker)
                        {
                            count += _UpdateTickerWithChildren(childTicker, ticker.Id);
                        }
                    }
                }
            }
        }
        else
        {
            // If it doesn't exist, add it (this can happen for new children)
            count += _AddTickerWithChildren(ticker, parentId);
        }

        return count;
    }

    public Task<int> RemoveTimeTickers(Guid[] tickerIds, CancellationToken cancellationToken = default)
    {
        var count = 0;
        foreach (var id in tickerIds)
        {
            // Remove ticker and all its children (cascade delete)
            if (_TimeTickers.TryRemove(id, out var removed))
            {
                count++;

                // Clean children index
                if (removed.ParentId.HasValue)
                {
                    _RemoveChildIndex(removed.ParentId.Value, removed.Id);
                }

                // Remove children
                var childrenIds = _GetChildrenIds(id);

                foreach (var childId in childrenIds)
                {
                    if (_TimeTickers.TryRemove(childId, out var child))
                    {
                        count++;
                        if (child.ParentId.HasValue)
                        {
                            _RemoveChildIndex(child.ParentId.Value, child.Id);
                        }
                    }
                }
            }
        }

        return Task.FromResult(count);
    }

    public Task ReleaseDeadNodeTimeTickerResources(
        string instanceIdentifier,
        CancellationToken cancellationToken = default
    )
    {
        var now = _clock.UtcNow;

        // Phase 1: release acquirable tickers for the dead node (match EF WhereCanAcquire(instanceIdentifier))
        var releasable = _TimeTickers
            .Values.Where(x =>
                (x.Status == TickerStatus.Idle || x.Status == TickerStatus.Queued)
                && (x.LockHolder == instanceIdentifier || x.LockedAt == null)
            )
            .ToArray();

        foreach (var ticker in releasable)
        {
            if (!_TimeTickers.TryGetValue(ticker.Id, out var currentTicker))
            {
                continue;
            }

            var updatedTicker = _CloneTicker(currentTicker);
            updatedTicker.LockHolder = null;
            updatedTicker.LockedAt = null;
            updatedTicker.Status = TickerStatus.Idle;
            updatedTicker.UpdatedAt = now;

            _TimeTickers.TryUpdate(ticker.Id, updatedTicker, currentTicker);
        }

        // Phase 2: mark in-progress tickers for that node as skipped
        var inProgress = _TimeTickers
            .Values.Where(x => x.LockHolder == instanceIdentifier && x.Status == TickerStatus.InProgress)
            .ToArray();

        foreach (var ticker in inProgress)
        {
            if (!_TimeTickers.TryGetValue(ticker.Id, out var currentTicker))
            {
                continue;
            }

            var updatedTicker = _CloneTicker(currentTicker);
            updatedTicker.Status = TickerStatus.Skipped;
            updatedTicker.SkippedReason = "Node is not alive!";
            updatedTicker.ExecutedAt = now;
            updatedTicker.UpdatedAt = now;

            _TimeTickers.TryUpdate(ticker.Id, updatedTicker, currentTicker);
        }

        return Task.CompletedTask;
    }

    #endregion

    #region Cron Ticker Methods

    public Task MigrateDefinedCronTickers(
        (string Function, string Expression)[] cronTickers,
        CancellationToken cancellationToken = default
    )
    {
        var now = _clock.UtcNow;

        foreach (var (function, expression) in cronTickers)
        {
            // Check if already exists (take snapshot for thread safety)
            var exists = _CronTickers.Values.ToArray().Any(x => x.Function == function && x.Expression == expression);
            if (!exists)
            {
                var id = Guid.NewGuid();
                var cronTicker = new TCronTicker
                {
                    Id = id,
                    Function = function,
                    Expression = expression,
                    InitIdentifier = $"MemoryTicker_Seeded_{id}",
                    CreatedAt = now,
                    UpdatedAt = now,
                    Request = Array.Empty<byte>(),
                };

                _CronTickers.TryAdd(id, cronTicker);
            }
        }

        return Task.CompletedTask;
    }

    public Task<CronTickerEntity[]> GetAllCronTickerExpressions(CancellationToken cancellationToken)
    {
        var result = _CronTickers.Values.Cast<CronTickerEntity>().ToArray();

        return Task.FromResult(result);
    }

    public Task<TCronTicker?> GetCronTickerById(Guid id, CancellationToken cancellationToken)
    {
        _CronTickers.TryGetValue(id, out var ticker);

        return Task.FromResult(ticker);
    }

    public Task<TCronTicker[]> GetCronTickers(
        Expression<Func<TCronTicker, bool>>? predicate,
        CancellationToken cancellationToken
    )
    {
        var compiledPredicate = predicate?.Compile();
        var query = _CronTickers.Values.AsEnumerable();

        if (compiledPredicate != null)
        {
            query = query.Where(compiledPredicate);
        }

        var results = query.OrderByDescending(x => x.CreatedAt).ToArray();

        return Task.FromResult(results);
    }

    public Task<PaginationResult<TCronTicker>> GetCronTickersPaginated(
        Expression<Func<TCronTicker, bool>>? predicate,
        int pageNumber,
        int pageSize,
        CancellationToken cancellationToken = default
    )
    {
        var compiledPredicate = predicate?.Compile();
        var query = _CronTickers.Values.AsEnumerable();

        if (compiledPredicate != null)
        {
            query = query.Where(compiledPredicate);
        }

        var totalCount = query.Count();

        var items = query
            .OrderByDescending(x => x.CreatedAt)
            .Skip((pageNumber - 1) * pageSize)
            .Take(pageSize)
            .ToArray();

        return Task.FromResult(
            new PaginationResult<TCronTicker>
            {
                Items = items,
                TotalCount = totalCount,
                PageNumber = pageNumber,
                PageSize = pageSize,
            }
        );
    }

    public Task<int> InsertCronTickers(TCronTicker[] tickers, CancellationToken cancellationToken)
    {
        var count = 0;
        foreach (var ticker in tickers)
        {
            if (_CronTickers.TryAdd(ticker.Id, ticker))
            {
                count++;
            }
        }

        return Task.FromResult(count);
    }

    public Task<int> UpdateCronTickers(TCronTicker[] cronTicker, CancellationToken cancellationToken)
    {
        var count = 0;
        foreach (var ticker in cronTicker)
        {
            if (_CronTickers.TryGetValue(ticker.Id, out var existing))
            {
                if (_CronTickers.TryUpdate(ticker.Id, ticker, existing))
                {
                    count++;
                }
            }
        }

        return Task.FromResult(count);
    }

    public Task<int> RemoveCronTickers(Guid[] cronTickerIds, CancellationToken cancellationToken)
    {
        var count = 0;
        foreach (var id in cronTickerIds)
        {
            if (_CronTickers.TryRemove(id, out _))
            {
                count++;
            }
        }

        return Task.FromResult(count);
    }

    #endregion

    #region Cron Occurrence Methods

    public Task<CronTickerOccurrenceEntity<TCronTicker>> GetEarliestAvailableCronOccurrence(
        Guid[] ids,
        CancellationToken cancellationToken = default
    )
    {
        var now = _clock.UtcNow;
        var mainSchedulerThreshold = now.AddSeconds(-1); // Main scheduler handles items within the 1-second window

        var query = _CronOccurrences.Values.AsEnumerable();

        if (ids != null && ids.Length > 0)
        {
            query = query.Where(x => ids.Contains(x.CronTickerId));
        }

        var occurrence = query
            .Where(x => _CanAcquireCronOccurrence(x))
            .Where(x => x.ExecutionTime >= mainSchedulerThreshold) // Only recent/upcoming tasks (not heavily overdue)
            .OrderBy(x => x.ExecutionTime)
            .FirstOrDefault();

        return Task.FromResult(occurrence!);
    }

    public async IAsyncEnumerable<CronTickerOccurrenceEntity<TCronTicker>> QueueCronTickerOccurrences(
        (DateTime Key, InternalManagerContext[] Items) cronTickerOccurrences,
        [EnumeratorCancellation] CancellationToken cancellationToken = default
    )
    {
        var now = _clock.UtcNow;

        foreach (var context in cronTickerOccurrences.Items)
        {
            cancellationToken.ThrowIfCancellationRequested();

            // Each cron occurrence should have a unique ID
            var occurrenceId = context.NextCronOccurrence?.Id ?? Guid.NewGuid();

            // Check if this specific occurrence already exists
            if (_CronOccurrences.TryGetValue(occurrenceId, out var existingOccurrence))
            {
                // Update existing occurrence (should be rare - only if re-queuing)
                var updatedOccurrence = _CloneCronOccurrence(existingOccurrence);
                updatedOccurrence.LockHolder = _lockHolder;
                updatedOccurrence.LockedAt = now;
                updatedOccurrence.UpdatedAt = now;
                updatedOccurrence.Status = TickerStatus.Queued;

                if (_CronOccurrences.TryUpdate(occurrenceId, updatedOccurrence, existingOccurrence))
                {
                    yield return updatedOccurrence;
                }
            }
            else
            {
                // Create new occurrence (normal case - each execution time gets its own occurrence)
                var newOccurrence = new CronTickerOccurrenceEntity<TCronTicker>
                {
                    Id = occurrenceId,
                    CronTickerId = context.Id,
                    ExecutionTime = cronTickerOccurrences.Key,
                    Status = TickerStatus.Queued,
                    LockHolder = _lockHolder,
                    LockedAt = now,
                    CreatedAt = context.NextCronOccurrence?.CreatedAt ?? now,
                    UpdatedAt = now,
                    RetryCount = 0,
                };

                // Try to get the cron ticker
                if (_CronTickers.TryGetValue(context.Id, out var cronTicker))
                {
                    newOccurrence.CronTicker = cronTicker;
                }

                if (_CronOccurrences.TryAdd(newOccurrence.Id, newOccurrence))
                {
                    yield return newOccurrence;
                }
            }
        }
    }

    public async IAsyncEnumerable<CronTickerOccurrenceEntity<TCronTicker>> QueueTimedOutCronTickerOccurrences(
        [EnumeratorCancellation] CancellationToken cancellationToken = default
    )
    {
        var now = _clock.UtcNow;
        var fallbackThreshold = now.AddSeconds(-1); // Fallback picks up tasks older than main 1-second window

        var occurrencesToUpdate = _CronOccurrences
            .Values.Where(x => x.Status is TickerStatus.Idle or TickerStatus.Queued)
            .Where(x => x.ExecutionTime <= fallbackThreshold) // Only tasks older than 1 second
            .ToArray();

        foreach (var occurrence in occurrencesToUpdate)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (_CronOccurrences.TryGetValue(occurrence.Id, out var existingOccurrence))
            {
                if (existingOccurrence.UpdatedAt <= occurrence.UpdatedAt)
                {
                    var updatedOccurrence = _CloneCronOccurrence(existingOccurrence);
                    updatedOccurrence.LockHolder = _lockHolder;
                    updatedOccurrence.LockedAt = now;
                    updatedOccurrence.UpdatedAt = now;
                    updatedOccurrence.Status = TickerStatus.InProgress;

                    if (_CronOccurrences.TryUpdate(occurrence.Id, updatedOccurrence, existingOccurrence))
                    {
                        yield return updatedOccurrence;
                    }
                }
            }
        }
    }

    public Task UpdateCronTickerOccurrence(
        InternalFunctionContext functionContext,
        CancellationToken cancellationToken = default
    )
    {
        if (_CronOccurrences.TryGetValue(functionContext.TickerId, out var occurrence))
        {
            var updatedOccurrence = _CloneCronOccurrence(occurrence);
            _ApplyFunctionContextToCronOccurrence(updatedOccurrence, functionContext);

            _CronOccurrences.TryUpdate(functionContext.TickerId, updatedOccurrence, occurrence);
        }

        return Task.CompletedTask;
    }

    public Task ReleaseAcquiredCronTickerOccurrences(
        Guid[] occurrenceIds,
        CancellationToken cancellationToken = default
    )
    {
        var now = _clock.UtcNow;
        var idsToRelease = occurrenceIds.Length == 0 ? _CronOccurrences.Keys.ToArray() : occurrenceIds;

        foreach (var id in idsToRelease)
        {
            if (_CronOccurrences.TryGetValue(id, out var occurrence))
            {
                if (_CanAcquireCronOccurrence(occurrence))
                {
                    var updatedOccurrence = _CloneCronOccurrence(occurrence);
                    updatedOccurrence.LockHolder = null;
                    updatedOccurrence.LockedAt = null;
                    updatedOccurrence.Status = TickerStatus.Idle;
                    updatedOccurrence.UpdatedAt = now;

                    _CronOccurrences.TryUpdate(id, updatedOccurrence, occurrence);
                }
            }
        }

        return Task.CompletedTask;
    }

    public Task<byte[]> GetCronTickerOccurrenceRequest(Guid tickerId, CancellationToken cancellationToken = default)
    {
        // Cron ticker occurrences don't have their own request, get it from the cron ticker
        if (_CronOccurrences.TryGetValue(tickerId, out var occurrence))
        {
            if (occurrence.CronTicker != null)
            {
                return Task.FromResult(occurrence.CronTicker.Request ?? Array.Empty<byte>());
            }

            if (_CronTickers.TryGetValue(occurrence.CronTickerId, out var cronTicker))
            {
                return Task.FromResult(cronTicker.Request ?? Array.Empty<byte>());
            }
        }

        return Task.FromResult(Array.Empty<byte>());
    }

    public Task UpdateCronTickerOccurrencesWithUnifiedContext(
        Guid[] timeTickerIds,
        InternalFunctionContext functionContext,
        CancellationToken cancellationToken = default
    )
    {
        foreach (var id in timeTickerIds)
        {
            if (_CronOccurrences.TryGetValue(id, out var occurrence))
            {
                var updatedOccurrence = _CloneCronOccurrence(occurrence);
                _ApplyFunctionContextToCronOccurrence(updatedOccurrence, functionContext);
                _CronOccurrences.TryUpdate(id, updatedOccurrence, occurrence);
            }
        }

        return Task.CompletedTask;
    }

    public Task ReleaseDeadNodeOccurrenceResources(
        string instanceIdentifier,
        CancellationToken cancellationToken = default
    )
    {
        var now = _clock.UtcNow;

        // Phase 1: release acquirable occurrences for the dead node (match EF WhereCanAcquire(instanceIdentifier))
        var releasable = _CronOccurrences
            .Values.Where(x =>
                (x.Status == TickerStatus.Idle || x.Status == TickerStatus.Queued)
                && (x.LockHolder == instanceIdentifier || x.LockedAt == null)
            )
            .ToArray();

        foreach (var occurrence in releasable)
        {
            if (!_CronOccurrences.TryGetValue(occurrence.Id, out var currentOccurrence))
            {
                continue;
            }

            var updatedOccurrence = _CloneCronOccurrence(currentOccurrence);
            updatedOccurrence.LockHolder = null;
            updatedOccurrence.LockedAt = null;
            updatedOccurrence.Status = TickerStatus.Idle;
            updatedOccurrence.UpdatedAt = now;

            _CronOccurrences.TryUpdate(occurrence.Id, updatedOccurrence, currentOccurrence);
        }

        // Phase 2: mark in-progress occurrences for that node as skipped
        var inProgress = _CronOccurrences
            .Values.Where(x => x.LockHolder == instanceIdentifier && x.Status == TickerStatus.InProgress)
            .ToArray();

        foreach (var occurrence in inProgress)
        {
            if (!_CronOccurrences.TryGetValue(occurrence.Id, out var currentOccurrence))
            {
                continue;
            }

            var updatedOccurrence = _CloneCronOccurrence(currentOccurrence);
            updatedOccurrence.Status = TickerStatus.Skipped;
            updatedOccurrence.SkippedReason = "Node is not alive!";
            updatedOccurrence.ExecutedAt = now;
            updatedOccurrence.UpdatedAt = now;

            _CronOccurrences.TryUpdate(occurrence.Id, updatedOccurrence, currentOccurrence);
        }

        return Task.CompletedTask;
    }

    public Task<CronTickerOccurrenceEntity<TCronTicker>[]> GetAllCronTickerOccurrences(
        Expression<Func<CronTickerOccurrenceEntity<TCronTicker>, bool>>? predicate,
        CancellationToken cancellationToken = default
    )
    {
        var compiledPredicate = predicate?.Compile();
        var query = _CronOccurrences.Values.AsEnumerable();

        if (compiledPredicate != null)
        {
            query = query.Where(compiledPredicate);
        }

        var results = query.OrderByDescending(x => x.CreatedAt).ToArray();

        return Task.FromResult(results);
    }

    public Task<PaginationResult<CronTickerOccurrenceEntity<TCronTicker>>> GetAllCronTickerOccurrencesPaginated(
        Expression<Func<CronTickerOccurrenceEntity<TCronTicker>, bool>> predicate,
        int pageNumber,
        int pageSize,
        CancellationToken cancellationToken = default
    )
    {
        var compiledPredicate = predicate?.Compile();
        var query = _CronOccurrences.Values.AsEnumerable();

        if (compiledPredicate != null)
        {
            query = query.Where(compiledPredicate);
        }

        var totalCount = query.Count();

        var items = query
            .OrderByDescending(x => x.CreatedAt)
            .Skip((pageNumber - 1) * pageSize)
            .Take(pageSize)
            .ToArray();

        return Task.FromResult(
            new PaginationResult<CronTickerOccurrenceEntity<TCronTicker>>
            {
                Items = items,
                TotalCount = totalCount,
                PageNumber = pageNumber,
                PageSize = pageSize,
            }
        );
    }

    public Task<int> InsertCronTickerOccurrences(
        CronTickerOccurrenceEntity<TCronTicker>[] cronTickerOccurrences,
        CancellationToken cancellationToken
    )
    {
        var count = 0;
        foreach (var occurrence in cronTickerOccurrences)
        {
            // Ensure navigation is populated for in-memory usage
            if (occurrence.CronTicker == null && _CronTickers.TryGetValue(occurrence.CronTickerId, out var cronTicker))
            {
                occurrence.CronTicker = cronTicker;
            }

            if (_CronOccurrences.TryAdd(occurrence.Id, occurrence))
            {
                count++;
            }
        }

        return Task.FromResult(count);
    }

    public Task<int> RemoveCronTickerOccurrences(Guid[] cronTickerOccurrences, CancellationToken cancellationToken)
    {
        var count = 0;
        foreach (var id in cronTickerOccurrences)
        {
            if (_CronOccurrences.TryRemove(id, out _))
            {
                count++;
            }
        }

        return Task.FromResult(count);
    }

    public Task<CronTickerOccurrenceEntity<TCronTicker>[]> AcquireImmediateCronOccurrencesAsync(
        Guid[] occurrenceIds,
        CancellationToken cancellationToken = default
    )
    {
        if (occurrenceIds == null || occurrenceIds.Length == 0)
        {
            return Task.FromResult(Array.Empty<CronTickerOccurrenceEntity<TCronTicker>>());
        }

        var now = _clock.UtcNow;
        var acquired = new List<CronTickerOccurrenceEntity<TCronTicker>>();

        foreach (var id in occurrenceIds)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (!_CronOccurrences.TryGetValue(id, out var occurrence))
            {
                continue;
            }

            if (!_CanAcquireCronOccurrence(occurrence))
            {
                continue;
            }

            var updated = _CloneCronOccurrence(occurrence);
            updated.LockHolder = _lockHolder;
            updated.LockedAt = now;
            updated.Status = TickerStatus.InProgress;
            updated.UpdatedAt = now;

            if (_CronOccurrences.TryUpdate(id, updated, occurrence))
            {
                acquired.Add(updated);
            }
        }

        return Task.FromResult(acquired.ToArray());
    }

    #endregion

    #region Helper Methods

    private TTimeTicker _BuildTickerHierarchy(TTimeTicker ticker)
    {
        var root = _CloneTicker(ticker);
        root.Children = _BuildChildrenHierarchy(ticker.Id);
        return root;
    }

    private static List<TTimeTicker> _BuildChildrenHierarchy(Guid parentId)
    {
        if (!_ChildrenIndex.TryGetValue(parentId, out var children) || children.IsEmpty)
        {
            return new List<TTimeTicker>();
        }

        var results = new List<TTimeTicker>(children.Count);

        foreach (var childId in children.Keys)
        {
            if (!_TimeTickers.TryGetValue(childId, out var child))
            {
                continue;
            }

            var clonedChild = _CloneTicker(child);
            clonedChild.Children = _BuildChildrenHierarchy(child.Id);
            results.Add(clonedChild);
        }

        return results;
    }

    // Matches EF Core's MappingExtensions.ForQueueTimeTickers but uses an in-memory children index
    private static TimeTickerEntity _ForQueueTimeTickers(TTimeTicker ticker)
    {
        var root = new TimeTickerEntity
        {
            Id = ticker.Id,
            Function = ticker.Function,
            Retries = ticker.Retries,
            RetryIntervals = ticker.RetryIntervals,
            UpdatedAt = ticker.UpdatedAt,
            ParentId = ticker.ParentId,
            ExecutionTime = ticker.ExecutionTime,
            Children = new List<TimeTickerEntity>(),
        };

        if (_ChildrenIndex.TryGetValue(ticker.Id, out var directChildren) && !directChildren.IsEmpty)
        {
            // Pre-size children collection to avoid repeated growth
            var children = new List<TimeTickerEntity>(directChildren.Count);

            foreach (var childId in directChildren.Keys)
            {
                if (!_TimeTickers.TryGetValue(childId, out var ch))
                {
                    continue;
                }

                // Only children with null ExecutionTime, matching EF mapping
                if (ch.ExecutionTime != null)
                {
                    continue;
                }

                var childEntity = new TimeTickerEntity
                {
                    Id = ch.Id,
                    Function = ch.Function,
                    Retries = ch.Retries,
                    RetryIntervals = ch.RetryIntervals,
                    RunCondition = ch.RunCondition,
                    Children = new List<TimeTickerEntity>(),
                };

                if (_ChildrenIndex.TryGetValue(ch.Id, out var grandChildren) && !grandChildren.IsEmpty)
                {
                    // Pre-size grandchildren collection
                    var grandChildList = new List<TimeTickerEntity>(grandChildren.Count);

                    foreach (var grandChildId in grandChildren.Keys)
                    {
                        if (!_TimeTickers.TryGetValue(grandChildId, out var gch))
                        {
                            continue;
                        }

                        grandChildList.Add(
                            new TimeTickerEntity
                            {
                                Id = gch.Id,
                                Function = gch.Function,
                                Retries = gch.Retries,
                                RetryIntervals = gch.RetryIntervals,
                                RunCondition = gch.RunCondition,
                            }
                        );
                    }

                    childEntity.Children = grandChildList;
                }

                children.Add(childEntity);
            }

            root.Children = children;
        }

        return root;
    }

    private static void _AddChildIndex(Guid parentId, Guid childId)
    {
        var children = _ChildrenIndex.GetOrAdd(parentId, _ => new ConcurrentDictionary<Guid, byte>());
        children.TryAdd(childId, 0);
    }

    private static void _RemoveChildIndex(Guid parentId, Guid childId)
    {
        if (!_ChildrenIndex.TryGetValue(parentId, out var children))
        {
            return;
        }

        children.TryRemove(childId, out _);

        // Optional: cleanup empty buckets
        if (children.IsEmpty)
        {
            _ChildrenIndex.TryRemove(parentId, out _);
        }
    }

    private static Guid[] _GetChildrenIds(Guid parentId)
    {
        if (!_ChildrenIndex.TryGetValue(parentId, out var children))
        {
            return Array.Empty<Guid>();
        }

        return children.Keys.ToArray();
    }

    private bool _CanAcquire(TTimeTicker ticker)
    {
        // Match EF provider logic: WhereCanAcquire
        // Can acquire if: (Status is Idle OR Queued) AND (LockHolder matches current OR LockedAt is null)
        return (
                (ticker.Status == TickerStatus.Idle || ticker.Status == TickerStatus.Queued)
                && ticker.LockHolder == _lockHolder
            )
            || (
                (ticker.Status == TickerStatus.Idle || ticker.Status == TickerStatus.Queued) && ticker.LockedAt == null
            );
    }

    private bool _CanAcquireCronOccurrence(CronTickerOccurrenceEntity<TCronTicker> occurrence)
    {
        // Match EF provider logic: WhereCanAcquire
        // Can acquire if: (Status is Idle OR Queued) AND (LockHolder matches current OR LockedAt is null)
        return (
                (occurrence.Status == TickerStatus.Idle || occurrence.Status == TickerStatus.Queued)
                && occurrence.LockHolder == _lockHolder
            )
            || (
                (occurrence.Status == TickerStatus.Idle || occurrence.Status == TickerStatus.Queued)
                && occurrence.LockedAt == null
            );
    }

    private static TTimeTicker _CloneTicker(TTimeTicker ticker)
    {
        var cloned = new TTimeTicker
        {
            Id = ticker.Id,
            Function = ticker.Function,
            Status = ticker.Status,
            Retries = ticker.Retries,
            RetryCount = ticker.RetryCount,
            ExecutionTime = ticker.ExecutionTime,
            InitIdentifier = ticker.InitIdentifier,
            LockHolder = ticker.LockHolder,
            LockedAt = ticker.LockedAt,
            ParentId = ticker.ParentId,
            Request = ticker.Request,
            ExceptionMessage = ticker.ExceptionMessage,
            SkippedReason = ticker.SkippedReason,
            ElapsedTime = ticker.ElapsedTime,
            RetryIntervals = ticker.RetryIntervals,
            RunCondition = ticker.RunCondition,
            ExecutedAt = ticker.ExecutedAt,
            CreatedAt = ticker.CreatedAt,
            UpdatedAt = ticker.UpdatedAt,
            Description = ticker.Description,
            Children = new List<TTimeTicker>(),
        };

        return cloned;
    }

    private static CronTickerOccurrenceEntity<TCronTicker> _CloneCronOccurrence(
        CronTickerOccurrenceEntity<TCronTicker> occurrence
    )
    {
        return new CronTickerOccurrenceEntity<TCronTicker>
        {
            Id = occurrence.Id,
            CronTicker = occurrence.CronTicker,
            CronTickerId = occurrence.CronTickerId,
            Status = occurrence.Status,
            RetryCount = occurrence.RetryCount,
            ExecutionTime = occurrence.ExecutionTime,
            LockHolder = occurrence.LockHolder,
            LockedAt = occurrence.LockedAt,
            ExceptionMessage = occurrence.ExceptionMessage,
            SkippedReason = occurrence.SkippedReason,
            ElapsedTime = occurrence.ElapsedTime,
            ExecutedAt = occurrence.ExecutedAt,
            CreatedAt = occurrence.CreatedAt,
            UpdatedAt = occurrence.UpdatedAt,
        };
    }

    private void _ApplyFunctionContextToTicker(TTimeTicker ticker, InternalFunctionContext context)
    {
        var propsToUpdate = context.GetPropsToUpdate();

        // STATUS / SKIPPED
        if (propsToUpdate.Contains(nameof(InternalFunctionContext.Status)) && context.Status != TickerStatus.Skipped)
        {
            ticker.Status = context.Status;
        }
        else if (propsToUpdate.Contains(nameof(InternalFunctionContext.Status)))
        {
            ticker.Status = context.Status;
            ticker.SkippedReason = context.ExceptionDetails;
        }

        // EXECUTED_AT
        if (propsToUpdate.Contains(nameof(InternalFunctionContext.ExecutedAt)))
        {
            ticker.ExecutedAt = context.ExecutedAt;
        }

        // EXCEPTION DETAILS
        if (
            propsToUpdate.Contains(nameof(InternalFunctionContext.ExceptionDetails))
            && context.Status != TickerStatus.Skipped
        )
        {
            ticker.ExceptionMessage = context.ExceptionDetails;
        }

        // ELAPSED_TIME
        if (propsToUpdate.Contains(nameof(InternalFunctionContext.ElapsedTime)))
        {
            ticker.ElapsedTime = context.ElapsedTime;
        }

        // RETRY COUNT
        if (propsToUpdate.Contains(nameof(InternalFunctionContext.RetryCount)))
        {
            ticker.RetryCount = context.RetryCount;
        }

        // RELEASE LOCK
        if (propsToUpdate.Contains(nameof(InternalFunctionContext.ReleaseLock)))
        {
            ticker.LockHolder = null;
            ticker.LockedAt = null;
        }

        // UPDATED_AT ALWAYS
        ticker.UpdatedAt = _clock.UtcNow;
    }

    private void _ApplyFunctionContextToCronOccurrence(
        CronTickerOccurrenceEntity<TCronTicker> occurrence,
        InternalFunctionContext context
    )
    {
        var propsToUpdate = context.GetPropsToUpdate();

        // STATUS / SKIPPED
        if (propsToUpdate.Contains(nameof(InternalFunctionContext.Status)) && context.Status != TickerStatus.Skipped)
        {
            occurrence.Status = context.Status;
        }
        else if (propsToUpdate.Contains(nameof(InternalFunctionContext.Status)))
        {
            occurrence.Status = context.Status;
            occurrence.SkippedReason = context.ExceptionDetails;
        }

        // EXECUTED_AT
        if (propsToUpdate.Contains(nameof(InternalFunctionContext.ExecutedAt)))
        {
            occurrence.ExecutedAt = context.ExecutedAt;
        }

        // EXCEPTION DETAILS
        if (
            propsToUpdate.Contains(nameof(InternalFunctionContext.ExceptionDetails))
            && context.Status != TickerStatus.Skipped
        )
        {
            occurrence.ExceptionMessage = context.ExceptionDetails;
        }

        // ELAPSED_TIME
        if (propsToUpdate.Contains(nameof(InternalFunctionContext.ElapsedTime)))
        {
            occurrence.ElapsedTime = context.ElapsedTime;
        }

        // RETRY COUNT
        if (propsToUpdate.Contains(nameof(InternalFunctionContext.RetryCount)))
        {
            occurrence.RetryCount = context.RetryCount;
        }

        // RELEASE LOCK
        if (propsToUpdate.Contains(nameof(InternalFunctionContext.ReleaseLock)))
        {
            occurrence.LockHolder = null;
            occurrence.LockedAt = null;
        }

        // UPDATED_AT ALWAYS
        occurrence.UpdatedAt = _clock.UtcNow;
    }

    #endregion
}
