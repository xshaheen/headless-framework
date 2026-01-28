using System.Runtime.CompilerServices;
using Headless.Ticker.Entities;
using Headless.Ticker.Enums;
using Headless.Ticker.Interfaces;
using Headless.Ticker.Models;
using Microsoft.EntityFrameworkCore;

namespace Headless.Ticker.Infrastructure;

internal abstract class BasePersistenceProvider<TDbContext, TTimeTicker, TCronTicker>(
    IDbContextFactory<TDbContext> dbContextFactory,
    ITickerClock clock,
    SchedulerOptionsBuilder optionsBuilder,
    ITickerQRedisContext redisContext
)
    where TDbContext : DbContext
    where TTimeTicker : TimeTickerEntity<TTimeTicker>, new()
    where TCronTicker : CronTickerEntity, new()
{
    protected IDbContextFactory<TDbContext> DbContextFactory { get; } = dbContextFactory;

    protected string LockHolder { get; } = optionsBuilder.NodeIdentifier;

    protected ITickerClock Clock { get; } = clock;

    protected ITickerQRedisContext RedisContext { get; } = redisContext;

    #region Core_Time_Ticker_Methods
    public async IAsyncEnumerable<TimeTickerEntity> QueueTimeTickers(
        TimeTickerEntity[] timeTickers,
        [EnumeratorCancellation] CancellationToken cancellationToken
    )
    {
        await using var dbContext = await DbContextFactory.CreateDbContextAsync(cancellationToken).AnyContext();

        var context = dbContext.Set<TTimeTicker>();
        var now = Clock.UtcNow;

        foreach (var timeTicker in timeTickers)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var updatedTicker = await context
                .Where(x => x.Id == timeTicker.Id)
                .Where(x => x.UpdatedAt == timeTicker.UpdatedAt)
                .ExecuteUpdateAsync(
                    prop =>
                        prop.SetProperty(x => x.LockHolder, LockHolder)
                            .SetProperty(x => x.LockedAt, now)
                            .SetProperty(x => x.UpdatedAt, now)
                            .SetProperty(x => x.Status, TickerStatus.Queued),
                    cancellationToken
                );

            if (updatedTicker <= 0)
            {
                continue;
            }

            timeTicker.UpdatedAt = now;
            timeTicker.LockHolder = LockHolder;
            timeTicker.LockedAt = now;
            timeTicker.Status = TickerStatus.Queued;

            yield return timeTicker;
        }
    }

    public async IAsyncEnumerable<TimeTickerEntity> QueueTimedOutTimeTickers(
        [EnumeratorCancellation] CancellationToken cancellationToken
    )
    {
        await using var dbContext = await DbContextFactory.CreateDbContextAsync(cancellationToken).AnyContext();

        var context = dbContext.Set<TTimeTicker>();
        var now = Clock.UtcNow;
        var fallbackThreshold = now.AddSeconds(-1); // Fallback picks up tasks older than main 1-second window

        var timeTickersToUpdate = await context
            .AsNoTracking()
            .Where(x => x.ExecutionTime != null)
            .Where(x => x.Status == TickerStatus.Idle || x.Status == TickerStatus.Queued)
            .Where(x => x.ExecutionTime <= fallbackThreshold) // Only tasks older than 1 second
            .Include(x => x.Children.Where(y => y.ExecutionTime == null))
            .Select(MappingExtensions.ForQueueTimeTickers<TTimeTicker>())
            .ToArrayAsync(cancellationToken)
            .AnyContext();

        foreach (var timeTicker in timeTickersToUpdate)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var affected = await context
                .Where(x => x.Id == timeTicker.Id && x.UpdatedAt <= timeTicker.UpdatedAt)
                .ExecuteUpdateAsync(
                    setter =>
                        setter
                            .SetProperty(x => x.LockHolder, LockHolder)
                            .SetProperty(x => x.LockedAt, now)
                            .SetProperty(x => x.UpdatedAt, now)
                            .SetProperty(x => x.Status, TickerStatus.InProgress),
                    cancellationToken
                )
                .AnyContext();

            if (affected <= 0)
            {
                continue;
            }

            yield return timeTicker;
        }
    }

    public async Task ReleaseAcquiredTimeTickers(Guid[] timeTickerIds, CancellationToken cancellationToken)
    {
        await using var dbContext = await DbContextFactory.CreateDbContextAsync(cancellationToken).AnyContext();

        var now = Clock.UtcNow;

        var baseQuery =
            timeTickerIds.Length == 0
                ? dbContext.Set<TTimeTicker>()
                : dbContext.Set<TTimeTicker>().Where(x => ((IEnumerable<Guid>)timeTickerIds).Contains(x.Id));

        await baseQuery
            .WhereCanAcquire(LockHolder)
            .ExecuteUpdateAsync(
                setter =>
                    setter
                        .SetProperty(x => x.LockHolder, _ => null)
                        .SetProperty(x => x.LockedAt, _ => null)
                        .SetProperty(x => x.Status, _ => TickerStatus.Idle)
                        .SetProperty(x => x.UpdatedAt, _ => now),
                cancellationToken
            )
            .AnyContext();
    }

    public async Task<int> UpdateTimeTicker(
        InternalFunctionContext functionContexts,
        CancellationToken cancellationToken
    )
    {
        await using var dbContext = await DbContextFactory.CreateDbContextAsync(cancellationToken).AnyContext();
        return await dbContext
            .Set<TTimeTicker>()
            .Where(x => x.Id == functionContexts.TickerId)
            .ExecuteUpdateAsync(setter => setter.UpdateTimeTicker(functionContexts, Clock.UtcNow), cancellationToken)
            .AnyContext();
    }

    public async Task UpdateTimeTickersWithUnifiedContext(
        Guid[] timeTickerIds,
        InternalFunctionContext functionContext,
        CancellationToken cancellationToken = default
    )
    {
        await using var dbContext = await DbContextFactory.CreateDbContextAsync(cancellationToken).AnyContext();

        await dbContext
            .Set<TTimeTicker>()
            .Where(x => ((IEnumerable<Guid>)timeTickerIds).Contains(x.Id))
            .ExecuteUpdateAsync(setter => setter.UpdateTimeTicker(functionContext, Clock.UtcNow), cancellationToken)
            .AnyContext();
    }

    public async Task<TimeTickerEntity[]> GetEarliestTimeTickers(CancellationToken cancellationToken)
    {
        await using var dbContext = await DbContextFactory.CreateDbContextAsync(cancellationToken).AnyContext();
        var now = Clock.UtcNow;

        // Define the window: ignore anything older than 1 second ago
        var oneSecondAgo = now.AddSeconds(-1);

        var baseQuery = dbContext
            .Set<TTimeTicker>()
            .AsNoTracking()
            .Where(x => x.ExecutionTime != null)
            .Where(x => x.ExecutionTime >= oneSecondAgo) // Ignore old tickers (fallback handles them)
            .WhereCanAcquire(LockHolder);

        // Find the earliest ticker within our window
        var minExecutionTime = await baseQuery
            .OrderBy(x => x.ExecutionTime)
            .Select(x => x.ExecutionTime)
            .FirstOrDefaultAsync(cancellationToken)
            .AnyContext();

        if (minExecutionTime == null)
        {
            return [];
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

        // Fetch all tickers within that complete second (this ensures we get all tickers in the same second)
        var maxExecutionTime = minSecond.AddSeconds(1);

        return await baseQuery
            .Include(x => x.Children.Where(y => y.ExecutionTime == null))
            .Where(x => x.ExecutionTime >= minSecond && x.ExecutionTime < maxExecutionTime)
            .OrderBy(x => x.ExecutionTime)
            .Select(MappingExtensions.ForQueueTimeTickers<TTimeTicker>())
            .ToArrayAsync(cancellationToken)
            .AnyContext();
    }

    public async Task<byte[]> GetTimeTickerRequest(Guid tickerId, CancellationToken cancellationToken = default)
    {
        await using var dbContext = await DbContextFactory.CreateDbContextAsync(cancellationToken).AnyContext();

        var request = await dbContext
            .Set<TTimeTicker>()
            .AsNoTracking()
            .Where(x => x.Id == tickerId)
            .Select(x => x.Request)
            .FirstOrDefaultAsync(cancellationToken)
            .AnyContext();

        return request ?? Array.Empty<byte>();
    }

    public async Task ReleaseDeadNodeTimeTickerResources(
        string instanceIdentifier,
        CancellationToken cancellationToken = default
    )
    {
        var now = Clock.UtcNow;
        await using var dbContext = await DbContextFactory.CreateDbContextAsync(cancellationToken).AnyContext();

        await dbContext
            .Set<TTimeTicker>()
            .WhereCanAcquire(instanceIdentifier)
            .ExecuteUpdateAsync(
                setter =>
                    setter
                        .SetProperty(x => x.LockHolder, _ => null)
                        .SetProperty(x => x.LockedAt, _ => null)
                        .SetProperty(x => x.Status, TickerStatus.Idle)
                        .SetProperty(x => x.UpdatedAt, now),
                cancellationToken
            )
            .AnyContext();

        await dbContext
            .Set<TTimeTicker>()
            .Where(x => x.LockHolder == instanceIdentifier && x.Status == TickerStatus.InProgress)
            .ExecuteUpdateAsync(
                setter =>
                    setter
                        .SetProperty(x => x.Status, TickerStatus.Skipped)
                        .SetProperty(x => x.SkippedReason, "Node is not alive!")
                        .SetProperty(x => x.ExecutedAt, now)
                        .SetProperty(x => x.UpdatedAt, now),
                cancellationToken
            )
            .AnyContext();
    }
    #endregion

    public async Task<TimeTickerEntity[]> AcquireImmediateTimeTickersAsync(
        Guid[] ids,
        CancellationToken cancellationToken = default
    )
    {
        if (ids == null || ids.Length == 0)
        {
            return [];
        }

        await using var dbContext = await DbContextFactory.CreateDbContextAsync(cancellationToken).AnyContext();
        var now = Clock.UtcNow;

        // Acquire and mark InProgress in a single update
        var affected = await dbContext
            .Set<TTimeTicker>()
            .Where(x => ((IEnumerable<Guid>)ids).Contains(x.Id))
            .WhereCanAcquire(LockHolder)
            .ExecuteUpdateAsync(
                setter =>
                    setter
                        .SetProperty(x => x.LockHolder, LockHolder)
                        .SetProperty(x => x.LockedAt, now)
                        .SetProperty(x => x.Status, TickerStatus.InProgress)
                        .SetProperty(x => x.UpdatedAt, now),
                cancellationToken
            )
            .AnyContext();

        if (affected == 0)
        {
            return [];
        }

        // Return the acquired tickers for immediate execution, with children
        return await dbContext
            .Set<TTimeTicker>()
            .AsNoTracking()
            .Where(x =>
                ((IEnumerable<Guid>)ids).Contains(x.Id)
                && x.LockHolder == LockHolder
                && x.Status == TickerStatus.InProgress
            )
            .Include(x => x.Children.Where(y => y.ExecutionTime == null))
            .Select(MappingExtensions.ForQueueTimeTickers<TTimeTicker>())
            .ToArrayAsync(cancellationToken)
            .AnyContext();
    }

    #region Core_Cron_Ticker_Methods
    public async Task MigrateDefinedCronTickers(
        (string Function, string Expression)[] cronTickers,
        CancellationToken cancellationToken = default
    )
    {
        await using var dbContext = await DbContextFactory.CreateDbContextAsync(cancellationToken).AnyContext();
        var now = Clock.UtcNow;

        var functions = cronTickers.Select(x => x.Function).ToArray();
        var cronSet = dbContext.Set<TCronTicker>();

        // Identify seeded cron tickers (created from in-memory definitions)
        const string seedPrefix = "MemoryTicker_Seeded_";

        var seededCron = await cronSet
            .Where(c => c.InitIdentifier != null && c.InitIdentifier.StartsWith(seedPrefix))
            .ToListAsync(cancellationToken)
            .AnyContext();

        var newFunctionSet = functions.ToHashSet(StringComparer.Ordinal);

        // Delete seeded cron tickers whose function no longer exists in the code definitions
        var seededToDelete = seededCron.Where(c => !newFunctionSet.Contains(c.Function)).Select(c => c.Id).ToArray();

        if (seededToDelete.Length > 0)
        {
            // Delete related occurrences first (if any), then the cron tickers
            await dbContext
                .Set<CronTickerOccurrenceEntity<TCronTicker>>()
                .Where(o => ((IEnumerable<Guid>)seededToDelete).Contains(o.CronTickerId))
                .ExecuteDeleteAsync(cancellationToken)
                .AnyContext();

            await cronSet
                .Where(c => ((IEnumerable<Guid>)seededToDelete).Contains(c.Id))
                .ExecuteDeleteAsync(cancellationToken)
                .AnyContext();
        }

        // Load existing (remaining) cron tickers for the current function set
        var existing = await cronSet
            .Where(c => ((IEnumerable<string>)functions).Contains(c.Function))
            .ToListAsync(cancellationToken)
            .AnyContext();

        var existingByFunction = existing
            .GroupBy(c => c.Function, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);

        foreach (var (function, expression) in cronTickers)
        {
            if (existingByFunction.TryGetValue(function, out var cron))
            {
                // Update expression if it changed
                if (!string.Equals(cron.Expression, expression, StringComparison.Ordinal))
                {
                    cron.Expression = expression;
                    cron.UpdatedAt = now;
                }
            }
            else
            {
                // Insert new seeded cron ticker
                var entity = new TCronTicker
                {
                    Id = Guid.NewGuid(),
                    Function = function,
                    Expression = expression,
                    InitIdentifier = $"MemoryTicker_Seeded_{function}",
                    CreatedAt = now,
                    UpdatedAt = now,
                    Request = Array.Empty<byte>(),
                };
                await cronSet.AddAsync(entity, cancellationToken).AnyContext();
            }
        }

        await dbContext.SaveChangesAsync(cancellationToken).AnyContext();
    }

    public async Task<CronTickerEntity[]> GetAllCronTickerExpressions(CancellationToken cancellationToken = default)
    {
        var result = await RedisContext.GetOrSetArrayAsync(
            cacheKey: "cron:expressions",
            factory: async (ct) =>
            {
                await using var dbContext = await DbContextFactory.CreateDbContextAsync(ct).AnyContext();
                return await dbContext
                    .Set<TCronTicker>()
                    .AsNoTracking()
                    .Select(MappingExtensions.ForCronTickerExpressions<CronTickerEntity>())
                    .ToArrayAsync(ct)
                    .AnyContext();
            },
            expiration: TimeSpan.FromMinutes(10),
            cancellationToken: cancellationToken
        );

        await using var dbContext = await DbContextFactory.CreateDbContextAsync(cancellationToken).AnyContext();

        return await dbContext
            .Set<TCronTicker>()
            .AsNoTracking()
            .Select(MappingExtensions.ForCronTickerExpressions<CronTickerEntity>())
            .ToArrayAsync(cancellationToken)
            .AnyContext();
    }
    #endregion

    #region Core_Cron_TickerOccurrence_Methods
    public async Task UpdateCronTickerOccurrence(
        InternalFunctionContext functionContext,
        CancellationToken cancellationToken
    )
    {
        await using var dbContext = await DbContextFactory.CreateDbContextAsync(cancellationToken).AnyContext();

        await dbContext
            .Set<CronTickerOccurrenceEntity<TCronTicker>>()
            .Where(x => x.Id == functionContext.TickerId)
            .ExecuteUpdateAsync(setter => setter.UpdateCronTickerOccurrence(functionContext), cancellationToken)
            .AnyContext();
    }

    public async IAsyncEnumerable<CronTickerOccurrenceEntity<TCronTicker>> QueueTimedOutCronTickerOccurrences(
        [EnumeratorCancellation] CancellationToken cancellationToken = default
    )
    {
        var now = Clock.UtcNow;
        var fallbackThreshold = now.AddSeconds(-1); // Fallback picks up tasks older than main 1-second window

        await using var dbContext = await DbContextFactory.CreateDbContextAsync(cancellationToken).AnyContext();

        var context = dbContext.Set<CronTickerOccurrenceEntity<TCronTicker>>();

        var cronTickersToUpdate = await context
            .AsNoTracking()
            .Include(x => x.CronTicker)
            .Where(x => x.Status == TickerStatus.Idle || x.Status == TickerStatus.Queued)
            .Where(x => x.ExecutionTime <= fallbackThreshold) // Only tasks older than 1 second
            .Select(
                MappingExtensions.ForQueueCronTickerOccurrence<CronTickerOccurrenceEntity<TCronTicker>, TCronTicker>()
            )
            .ToArrayAsync(cancellationToken)
            .AnyContext();

        foreach (var cronTickerOccurrence in cronTickersToUpdate)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var affected = await context
                .Where(x => x.Id == cronTickerOccurrence.Id && x.UpdatedAt == cronTickerOccurrence.UpdatedAt)
                .ExecuteUpdateAsync(
                    setter =>
                        setter
                            .SetProperty(x => x.LockHolder, LockHolder)
                            .SetProperty(x => x.LockedAt, now)
                            .SetProperty(x => x.UpdatedAt, now)
                            .SetProperty(x => x.Status, TickerStatus.InProgress),
                    cancellationToken
                )
                .AnyContext();

            if (affected <= 0)
            {
                continue;
            }

            yield return cronTickerOccurrence;
        }
    }

    public async Task ReleaseDeadNodeOccurrenceResources(
        string instanceIdentifier,
        CancellationToken cancellationToken = default
    )
    {
        var now = Clock.UtcNow;
        await using var dbContext = await DbContextFactory.CreateDbContextAsync(cancellationToken).AnyContext();

        await dbContext
            .Set<CronTickerOccurrenceEntity<TCronTicker>>()
            .WhereCanAcquire(instanceIdentifier)
            .ExecuteUpdateAsync(
                setter =>
                    setter
                        .SetProperty(x => x.LockHolder, _ => null)
                        .SetProperty(x => x.LockedAt, _ => null)
                        .SetProperty(x => x.Status, TickerStatus.Idle)
                        .SetProperty(x => x.UpdatedAt, now),
                cancellationToken
            )
            .AnyContext();

        await dbContext
            .Set<CronTickerOccurrenceEntity<TCronTicker>>()
            .Where(x => x.LockHolder == instanceIdentifier && x.Status == TickerStatus.InProgress)
            .ExecuteUpdateAsync(
                setter =>
                    setter
                        .SetProperty(x => x.Status, TickerStatus.Skipped)
                        .SetProperty(x => x.SkippedReason, "Node is not alive!")
                        .SetProperty(x => x.ExecutedAt, now)
                        .SetProperty(x => x.UpdatedAt, now),
                cancellationToken
            )
            .AnyContext();
    }

    public async Task ReleaseAcquiredCronTickerOccurrences(
        Guid[] occurrenceIds,
        CancellationToken cancellationToken = default
    )
    {
        var now = Clock.UtcNow;
        await using var dbContext = await DbContextFactory.CreateDbContextAsync(cancellationToken).AnyContext();

        var baseQuery =
            occurrenceIds.Length == 0
                ? dbContext.Set<CronTickerOccurrenceEntity<TCronTicker>>()
                : dbContext
                    .Set<CronTickerOccurrenceEntity<TCronTicker>>()
                    .Where(x => ((IEnumerable<Guid>)occurrenceIds).Contains(x.Id));

        await baseQuery
            .WhereCanAcquire(LockHolder)
            .ExecuteUpdateAsync(
                setter =>
                    setter
                        .SetProperty(x => x.LockHolder, _ => null)
                        .SetProperty(x => x.LockedAt, _ => null)
                        .SetProperty(x => x.Status, TickerStatus.Idle)
                        .SetProperty(x => x.UpdatedAt, now),
                cancellationToken
            )
            .AnyContext();
    }

    public async IAsyncEnumerable<CronTickerOccurrenceEntity<TCronTicker>> QueueCronTickerOccurrences(
        (DateTime Key, InternalManagerContext[] Items) cronTickerOccurrences,
        [EnumeratorCancellation] CancellationToken cancellationToken = default
    )
    {
        var now = Clock.UtcNow;
        var executionTime = cronTickerOccurrences.Key;

        await using var dbContext = await DbContextFactory.CreateDbContextAsync(cancellationToken).AnyContext();

        var context = dbContext.Set<CronTickerOccurrenceEntity<TCronTicker>>();

        foreach (var item in cronTickerOccurrences.Items)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (item.NextCronOccurrence is null)
            {
                var itemToAdd = new CronTickerOccurrenceEntity<TCronTicker>
                {
                    Id = Guid.NewGuid(),
                    Status = TickerStatus.Queued,
                    LockHolder = LockHolder,
                    ExecutionTime = executionTime,
                    CronTickerId = item.Id,
                    LockedAt = now,
                    CreatedAt = now,
                    UpdatedAt = now,
                };

                var affectAdded = await context
                    .Upsert(itemToAdd)
                    .On(x => new { x.ExecutionTime, x.CronTickerId })
                    .NoUpdate()
                    .RunAsync(cancellationToken)
                    .AnyContext();

                if (affectAdded <= 0)
                {
                    continue;
                }

                itemToAdd.CronTicker = new TCronTicker
                {
                    Id = item.Id,
                    Function = item.FunctionName,
                    InitIdentifier = LockHolder,
                    Expression = item.Expression,
                    Retries = item.Retries,
                    RetryIntervals = item.RetryIntervals,
                };
                yield return itemToAdd;
            }
            else
            {
                var affectedUpdate = await context
                    .Where(x => x.Id == item.NextCronOccurrence.Id)
                    .Where(x => x.ExecutionTime == executionTime)
                    .WhereCanAcquire(LockHolder)
                    .ExecuteUpdateAsync(
                        prop =>
                            prop.SetProperty(y => y.LockHolder, LockHolder)
                                .SetProperty(y => y.LockedAt, now)
                                .SetProperty(y => y.UpdatedAt, now)
                                .SetProperty(y => y.Status, TickerStatus.Queued),
                        cancellationToken
                    )
                    .AnyContext();

                if (affectedUpdate <= 0)
                {
                    continue;
                }

                yield return new CronTickerOccurrenceEntity<TCronTicker>
                {
                    Id = item.NextCronOccurrence.Id,
                    CronTickerId = item.Id,
                    ExecutionTime = executionTime,
                    Status = TickerStatus.Queued,
                    LockHolder = LockHolder,
                    LockedAt = now,
                    UpdatedAt = now,
                    CreatedAt = item.NextCronOccurrence.CreatedAt,
                    CronTicker = new TCronTicker
                    {
                        Id = item.Id,
                        Function = item.FunctionName,
                        InitIdentifier = LockHolder,
                        Expression = item.Expression,
                        Retries = item.Retries,
                        RetryIntervals = item.RetryIntervals,
                    },
                };
            }
        }
    }

    public async Task<CronTickerOccurrenceEntity<TCronTicker>> GetEarliestAvailableCronOccurrence(
        Guid[] ids,
        CancellationToken cancellationToken = default
    )
    {
        var now = Clock.UtcNow;
        var mainSchedulerThreshold = now.AddSeconds(-1);
        await using var dbContext = await DbContextFactory.CreateDbContextAsync(cancellationToken).AnyContext();

        var occurrence = await dbContext
            .Set<CronTickerOccurrenceEntity<TCronTicker>>()
            .AsNoTracking()
            .Include(x => x.CronTicker)
            .Where(x => ((IEnumerable<Guid>)ids).Contains(x.CronTickerId))
            .Where(x => x.ExecutionTime >= mainSchedulerThreshold) // Only items within the 1-second main scheduler window
            .WhereCanAcquire(LockHolder)
            .OrderBy(x => x.ExecutionTime)
            .Select(
                MappingExtensions.ForLatestQueuedCronTickerOccurrence<
                    CronTickerOccurrenceEntity<TCronTicker>,
                    TCronTicker
                >()
            )
            .FirstOrDefaultAsync(cancellationToken)
            .AnyContext();

        return occurrence!;
    }

    public async Task<byte[]> GetCronTickerOccurrenceRequest(
        Guid tickerId,
        CancellationToken cancellationToken = default
    )
    {
        await using var dbContext = await DbContextFactory.CreateDbContextAsync(cancellationToken).AnyContext();

        var request = await dbContext
            .Set<CronTickerOccurrenceEntity<TCronTicker>>()
            .AsNoTracking()
            .Include(x => x.CronTicker)
            .Where(x => x.Id == tickerId)
            .Select(x => x.CronTicker.Request)
            .FirstOrDefaultAsync(cancellationToken)
            .AnyContext();

        return request ?? Array.Empty<byte>();
    }

    public async Task UpdateCronTickerOccurrencesWithUnifiedContext(
        Guid[] cronOccurrenceIds,
        InternalFunctionContext functionContext,
        CancellationToken cancellationToken = default
    )
    {
        await using var dbContext = await DbContextFactory.CreateDbContextAsync(cancellationToken).AnyContext();

        await dbContext
            .Set<CronTickerOccurrenceEntity<TCronTicker>>()
            .Where(x => ((IEnumerable<Guid>)cronOccurrenceIds).Contains(x.Id))
            .ExecuteUpdateAsync(setter => setter.UpdateCronTickerOccurrence(functionContext), cancellationToken)
            .AnyContext();
    }

    #endregion
}
