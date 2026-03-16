using System.Runtime.CompilerServices;
using Headless.Jobs.Entities;
using Headless.Jobs.Enums;
using Headless.Jobs.Interfaces;
using Headless.Jobs.Models;
using Microsoft.EntityFrameworkCore;

namespace Headless.Jobs.Infrastructure;

internal abstract class BasePersistenceProvider<TDbContext, TTimeJob, TCronJob>(
    IDbContextFactory<TDbContext> dbContextFactory,
    IJobClock clock,
    SchedulerOptionsBuilder optionsBuilder,
    IJobsRedisContext redisContext
)
    where TDbContext : DbContext
    where TTimeJob : TimeJobEntity<TTimeJob>, new()
    where TCronJob : CronJobEntity, new()
{
    protected IDbContextFactory<TDbContext> DbContextFactory { get; } = dbContextFactory;

    protected string LockHolder { get; } = optionsBuilder.NodeIdentifier;

    protected IJobClock Clock { get; } = clock;

    protected IJobsRedisContext RedisContext { get; } = redisContext;

    #region Core_Time_Ticker_Methods
    public async IAsyncEnumerable<TimeJobEntity> QueueTimeJobs(
        TimeJobEntity[] timeJobs,
        [EnumeratorCancellation] CancellationToken cancellationToken
    )
    {
        await using var dbContext = await DbContextFactory
            .CreateDbContextAsync(cancellationToken)
            .ConfigureAwait(false);

        var context = dbContext.Set<TTimeJob>();
        var now = Clock.UtcNow;

        foreach (var timeJob in timeJobs)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var updatedTicker = await context
                .Where(x => x.Id == timeJob.Id)
                .Where(x => x.UpdatedAt == timeJob.UpdatedAt)
                .ExecuteUpdateAsync(
                    prop =>
                        prop.SetProperty(x => x.LockHolder, LockHolder)
                            .SetProperty(x => x.LockedAt, now)
                            .SetProperty(x => x.UpdatedAt, now)
                            .SetProperty(x => x.Status, JobStatus.Queued),
                    cancellationToken
                );

            if (updatedTicker <= 0)
            {
                continue;
            }

            timeJob.UpdatedAt = now;
            timeJob.LockHolder = LockHolder;
            timeJob.LockedAt = now;
            timeJob.Status = JobStatus.Queued;

            yield return timeJob;
        }
    }

    public async IAsyncEnumerable<TimeJobEntity> QueueTimedOutTimeJobs(
        [EnumeratorCancellation] CancellationToken cancellationToken
    )
    {
        await using var dbContext = await DbContextFactory
            .CreateDbContextAsync(cancellationToken)
            .ConfigureAwait(false);

        var context = dbContext.Set<TTimeJob>();
        var now = Clock.UtcNow;
        var fallbackThreshold = now.AddSeconds(-1); // Fallback picks up tasks older than main 1-second window

        var timeJobsToUpdate = await context
            .AsNoTracking()
            .Where(x => x.ExecutionTime != null)
            .Where(x => x.Status == JobStatus.Idle || x.Status == JobStatus.Queued)
            .Where(x => x.ExecutionTime <= fallbackThreshold) // Only tasks older than 1 second
            .Include(x => x.Children.Where(y => y.ExecutionTime == null))
            .Select(MappingExtensions.ForQueueTimeJobs<TTimeJob>())
            .ToArrayAsync(cancellationToken)
            .ConfigureAwait(false);

        foreach (var timeJob in timeJobsToUpdate)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var affected = await context
                .Where(x => x.Id == timeJob.Id && x.UpdatedAt <= timeJob.UpdatedAt)
                .ExecuteUpdateAsync(
                    setter =>
                        setter
                            .SetProperty(x => x.LockHolder, LockHolder)
                            .SetProperty(x => x.LockedAt, now)
                            .SetProperty(x => x.UpdatedAt, now)
                            .SetProperty(x => x.Status, JobStatus.InProgress),
                    cancellationToken
                )
                .ConfigureAwait(false);

            if (affected <= 0)
            {
                continue;
            }

            yield return timeJob;
        }
    }

    public async Task ReleaseAcquiredTimeJobs(Guid[] timeJobIds, CancellationToken cancellationToken)
    {
        await using var dbContext = await DbContextFactory
            .CreateDbContextAsync(cancellationToken)
            .ConfigureAwait(false);

        var now = Clock.UtcNow;

        var baseQuery =
            timeJobIds.Length == 0
                ? dbContext.Set<TTimeJob>()
                : dbContext.Set<TTimeJob>().Where(x => ((IEnumerable<Guid>)timeJobIds).Contains(x.Id));

        await baseQuery
            .WhereCanAcquire(LockHolder)
            .ExecuteUpdateAsync(
                setter =>
                    setter
                        .SetProperty(x => x.LockHolder, _ => null)
                        .SetProperty(x => x.LockedAt, _ => null)
                        .SetProperty(x => x.Status, _ => JobStatus.Idle)
                        .SetProperty(x => x.UpdatedAt, _ => now),
                cancellationToken
            )
            .ConfigureAwait(false);
    }

    public async Task<int> UpdateTimeJob(
        InternalFunctionContext functionContexts,
        CancellationToken cancellationToken
    )
    {
        await using var dbContext = await DbContextFactory
            .CreateDbContextAsync(cancellationToken)
            .ConfigureAwait(false);
        return await dbContext
            .Set<TTimeJob>()
            .Where(x => x.Id == functionContexts.JobId)
            .ExecuteUpdateAsync(setter => setter.UpdateTimeJob(functionContexts, Clock.UtcNow), cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task UpdateTimeJobsWithUnifiedContext(
        Guid[] timeJobIds,
        InternalFunctionContext functionContext,
        CancellationToken cancellationToken = default
    )
    {
        await using var dbContext = await DbContextFactory
            .CreateDbContextAsync(cancellationToken)
            .ConfigureAwait(false);

        await dbContext
            .Set<TTimeJob>()
            .Where(x => ((IEnumerable<Guid>)timeJobIds).Contains(x.Id))
            .ExecuteUpdateAsync(setter => setter.UpdateTimeJob(functionContext, Clock.UtcNow), cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<TimeJobEntity[]> GetEarliestTimeJobs(CancellationToken cancellationToken)
    {
        await using var dbContext = await DbContextFactory
            .CreateDbContextAsync(cancellationToken)
            .ConfigureAwait(false);
        var now = Clock.UtcNow;

        // Define the window: ignore anything older than 1 second ago
        var oneSecondAgo = now.AddSeconds(-1);

        var baseQuery = dbContext
            .Set<TTimeJob>()
            .AsNoTracking()
            .Where(x => x.ExecutionTime != null)
            .Where(x => x.ExecutionTime >= oneSecondAgo) // Ignore old jobs (fallback handles them)
            .WhereCanAcquire(LockHolder);

        // Find the earliest job within our window
        var minExecutionTime = await baseQuery
            .OrderBy(x => x.ExecutionTime)
            .Select(x => x.ExecutionTime)
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);

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

        // Fetch all jobs within that complete second (this ensures we get all jobs in the same second)
        var maxExecutionTime = minSecond.AddSeconds(1);

        return await baseQuery
            .Include(x => x.Children.Where(y => y.ExecutionTime == null))
            .Where(x => x.ExecutionTime >= minSecond && x.ExecutionTime < maxExecutionTime)
            .OrderBy(x => x.ExecutionTime)
            .Select(MappingExtensions.ForQueueTimeJobs<TTimeJob>())
            .ToArrayAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<byte[]> GetTimeJobRequest(Guid jobId, CancellationToken cancellationToken = default)
    {
        await using var dbContext = await DbContextFactory
            .CreateDbContextAsync(cancellationToken)
            .ConfigureAwait(false);

        var request = await dbContext
            .Set<TTimeJob>()
            .AsNoTracking()
            .Where(x => x.Id == jobId)
            .Select(x => x.Request)
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);

        return request ?? Array.Empty<byte>();
    }

    public async Task ReleaseDeadNodeTimeJobResources(
        string instanceIdentifier,
        CancellationToken cancellationToken = default
    )
    {
        var now = Clock.UtcNow;
        await using var dbContext = await DbContextFactory
            .CreateDbContextAsync(cancellationToken)
            .ConfigureAwait(false);

        await dbContext
            .Set<TTimeJob>()
            .WhereCanAcquire(instanceIdentifier)
            .ExecuteUpdateAsync(
                setter =>
                    setter
                        .SetProperty(x => x.LockHolder, _ => null)
                        .SetProperty(x => x.LockedAt, _ => null)
                        .SetProperty(x => x.Status, JobStatus.Idle)
                        .SetProperty(x => x.UpdatedAt, now),
                cancellationToken
            )
            .ConfigureAwait(false);

        await dbContext
            .Set<TTimeJob>()
            .Where(x => x.LockHolder == instanceIdentifier && x.Status == JobStatus.InProgress)
            .ExecuteUpdateAsync(
                setter =>
                    setter
                        .SetProperty(x => x.Status, JobStatus.Skipped)
                        .SetProperty(x => x.SkippedReason, "Node is not alive!")
                        .SetProperty(x => x.ExecutedAt, now)
                        .SetProperty(x => x.UpdatedAt, now),
                cancellationToken
            )
            .ConfigureAwait(false);
    }
    #endregion

    public async Task<TimeJobEntity[]> AcquireImmediateTimeJobsAsync(
        Guid[] ids,
        CancellationToken cancellationToken = default
    )
    {
        if (ids == null || ids.Length == 0)
        {
            return [];
        }

        await using var dbContext = await DbContextFactory
            .CreateDbContextAsync(cancellationToken)
            .ConfigureAwait(false);
        var now = Clock.UtcNow;

        // Acquire and mark InProgress in a single update
        var affected = await dbContext
            .Set<TTimeJob>()
            .Where(x => ((IEnumerable<Guid>)ids).Contains(x.Id))
            .WhereCanAcquire(LockHolder)
            .ExecuteUpdateAsync(
                setter =>
                    setter
                        .SetProperty(x => x.LockHolder, LockHolder)
                        .SetProperty(x => x.LockedAt, now)
                        .SetProperty(x => x.Status, JobStatus.InProgress)
                        .SetProperty(x => x.UpdatedAt, now),
                cancellationToken
            )
            .ConfigureAwait(false);

        if (affected == 0)
        {
            return [];
        }

        // Return the acquired jobs for immediate execution, with children
        return await dbContext
            .Set<TTimeJob>()
            .AsNoTracking()
            .Where(x =>
                ((IEnumerable<Guid>)ids).Contains(x.Id)
                && x.LockHolder == LockHolder
                && x.Status == JobStatus.InProgress
            )
            .Include(x => x.Children.Where(y => y.ExecutionTime == null))
            .Select(MappingExtensions.ForQueueTimeJobs<TTimeJob>())
            .ToArrayAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    #region Core_Cron_Ticker_Methods
    public async Task MigrateDefinedCronJobs(
        (string Function, string Expression)[] cronJobs,
        CancellationToken cancellationToken = default
    )
    {
        await using var dbContext = await DbContextFactory
            .CreateDbContextAsync(cancellationToken)
            .ConfigureAwait(false);
        var now = Clock.UtcNow;

        var functions = cronJobs.Select(x => x.Function).ToArray();
        var cronSet = dbContext.Set<TCronJob>();

        // Identify seeded cron jobs (created from in-memory definitions)
        const string seedPrefix = "MemoryTicker_Seeded_";

        var seededCron = await cronSet
            .Where(c => c.InitIdentifier != null && c.InitIdentifier.StartsWith(seedPrefix))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var newFunctionSet = functions.ToHashSet(StringComparer.Ordinal);

        // Delete seeded cron jobs whose function no longer exists in the code definitions
        var seededToDelete = seededCron.Where(c => !newFunctionSet.Contains(c.Function)).Select(c => c.Id).ToArray();

        if (seededToDelete.Length > 0)
        {
            // Delete related occurrences first (if any), then the cron jobs
            await dbContext
                .Set<CronJobOccurrenceEntity<TCronJob>>()
                .Where(o => ((IEnumerable<Guid>)seededToDelete).Contains(o.CronJobId))
                .ExecuteDeleteAsync(cancellationToken)
                .ConfigureAwait(false);

            await cronSet
                .Where(c => ((IEnumerable<Guid>)seededToDelete).Contains(c.Id))
                .ExecuteDeleteAsync(cancellationToken)
                .ConfigureAwait(false);
        }

        // Load existing (remaining) cron jobs for the current function set
        var existing = await cronSet
            .Where(c => ((IEnumerable<string>)functions).Contains(c.Function))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var existingByFunction = existing
            .GroupBy(c => c.Function, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);

        foreach (var (function, expression) in cronJobs)
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
                // Insert new seeded cron job
                var entity = new TCronJob
                {
                    Id = Guid.NewGuid(),
                    Function = function,
                    Expression = expression,
                    InitIdentifier = $"MemoryTicker_Seeded_{function}",
                    CreatedAt = now,
                    UpdatedAt = now,
                    Request = Array.Empty<byte>(),
                };
                await cronSet.AddAsync(entity, cancellationToken).ConfigureAwait(false);
            }
        }

        await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<CronJobEntity[]> GetAllCronJobExpressions(CancellationToken cancellationToken = default)
    {
        var result = await RedisContext.GetOrSetArrayAsync(
            cacheKey: "cron:expressions",
            factory: async (ct) =>
            {
                await using var dbContext = await DbContextFactory.CreateDbContextAsync(ct).ConfigureAwait(false);
                return await dbContext
                    .Set<TCronJob>()
                    .AsNoTracking()
                    .Select(MappingExtensions.ForCronJobExpressions<CronJobEntity>())
                    .ToArrayAsync(ct)
                    .ConfigureAwait(false);
            },
            expiration: TimeSpan.FromMinutes(10),
            cancellationToken: cancellationToken
        );

        await using var dbContext = await DbContextFactory
            .CreateDbContextAsync(cancellationToken)
            .ConfigureAwait(false);

        return await dbContext
            .Set<TCronJob>()
            .AsNoTracking()
            .Select(MappingExtensions.ForCronJobExpressions<CronJobEntity>())
            .ToArrayAsync(cancellationToken)
            .ConfigureAwait(false);
    }
    #endregion

    #region Core_Cron_TickerOccurrence_Methods
    public async Task UpdateCronJobOccurrence(
        InternalFunctionContext functionContext,
        CancellationToken cancellationToken
    )
    {
        await using var dbContext = await DbContextFactory
            .CreateDbContextAsync(cancellationToken)
            .ConfigureAwait(false);

        await dbContext
            .Set<CronJobOccurrenceEntity<TCronJob>>()
            .Where(x => x.Id == functionContext.JobId)
            .ExecuteUpdateAsync(setter => setter.UpdateCronJobOccurrence(functionContext), cancellationToken)
            .ConfigureAwait(false);
    }

    public async IAsyncEnumerable<CronJobOccurrenceEntity<TCronJob>> QueueTimedOutCronJobOccurrences(
        [EnumeratorCancellation] CancellationToken cancellationToken = default
    )
    {
        var now = Clock.UtcNow;
        var fallbackThreshold = now.AddSeconds(-1); // Fallback picks up tasks older than main 1-second window

        await using var dbContext = await DbContextFactory
            .CreateDbContextAsync(cancellationToken)
            .ConfigureAwait(false);

        var context = dbContext.Set<CronJobOccurrenceEntity<TCronJob>>();

        var cronJobsToUpdate = await context
            .AsNoTracking()
            .Include(x => x.CronJob)
            .Where(x => x.Status == JobStatus.Idle || x.Status == JobStatus.Queued)
            .Where(x => x.ExecutionTime <= fallbackThreshold) // Only tasks older than 1 second
            .Select(
                MappingExtensions.ForQueueCronJobOccurrence<CronJobOccurrenceEntity<TCronJob>, TCronJob>()
            )
            .ToArrayAsync(cancellationToken)
            .ConfigureAwait(false);

        foreach (var cronJobOccurrence in cronJobsToUpdate)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var affected = await context
                .Where(x => x.Id == cronJobOccurrence.Id && x.UpdatedAt == cronJobOccurrence.UpdatedAt)
                .ExecuteUpdateAsync(
                    setter =>
                        setter
                            .SetProperty(x => x.LockHolder, LockHolder)
                            .SetProperty(x => x.LockedAt, now)
                            .SetProperty(x => x.UpdatedAt, now)
                            .SetProperty(x => x.Status, JobStatus.InProgress),
                    cancellationToken
                )
                .ConfigureAwait(false);

            if (affected <= 0)
            {
                continue;
            }

            yield return cronJobOccurrence;
        }
    }

    public async Task ReleaseDeadNodeOccurrenceResources(
        string instanceIdentifier,
        CancellationToken cancellationToken = default
    )
    {
        var now = Clock.UtcNow;
        await using var dbContext = await DbContextFactory
            .CreateDbContextAsync(cancellationToken)
            .ConfigureAwait(false);

        await dbContext
            .Set<CronJobOccurrenceEntity<TCronJob>>()
            .WhereCanAcquire(instanceIdentifier)
            .ExecuteUpdateAsync(
                setter =>
                    setter
                        .SetProperty(x => x.LockHolder, _ => null)
                        .SetProperty(x => x.LockedAt, _ => null)
                        .SetProperty(x => x.Status, JobStatus.Idle)
                        .SetProperty(x => x.UpdatedAt, now),
                cancellationToken
            )
            .ConfigureAwait(false);

        await dbContext
            .Set<CronJobOccurrenceEntity<TCronJob>>()
            .Where(x => x.LockHolder == instanceIdentifier && x.Status == JobStatus.InProgress)
            .ExecuteUpdateAsync(
                setter =>
                    setter
                        .SetProperty(x => x.Status, JobStatus.Skipped)
                        .SetProperty(x => x.SkippedReason, "Node is not alive!")
                        .SetProperty(x => x.ExecutedAt, now)
                        .SetProperty(x => x.UpdatedAt, now),
                cancellationToken
            )
            .ConfigureAwait(false);
    }

    public async Task ReleaseAcquiredCronJobOccurrences(
        Guid[] occurrenceIds,
        CancellationToken cancellationToken = default
    )
    {
        var now = Clock.UtcNow;
        await using var dbContext = await DbContextFactory
            .CreateDbContextAsync(cancellationToken)
            .ConfigureAwait(false);

        var baseQuery =
            occurrenceIds.Length == 0
                ? dbContext.Set<CronJobOccurrenceEntity<TCronJob>>()
                : dbContext
                    .Set<CronJobOccurrenceEntity<TCronJob>>()
                    .Where(x => ((IEnumerable<Guid>)occurrenceIds).Contains(x.Id));

        await baseQuery
            .WhereCanAcquire(LockHolder)
            .ExecuteUpdateAsync(
                setter =>
                    setter
                        .SetProperty(x => x.LockHolder, _ => null)
                        .SetProperty(x => x.LockedAt, _ => null)
                        .SetProperty(x => x.Status, JobStatus.Idle)
                        .SetProperty(x => x.UpdatedAt, now),
                cancellationToken
            )
            .ConfigureAwait(false);
    }

    public async IAsyncEnumerable<CronJobOccurrenceEntity<TCronJob>> QueueCronJobOccurrences(
        (DateTime Key, InternalManagerContext[] Items) cronJobOccurrences,
        [EnumeratorCancellation] CancellationToken cancellationToken = default
    )
    {
        var now = Clock.UtcNow;
        var executionTime = cronJobOccurrences.Key;

        await using var dbContext = await DbContextFactory
            .CreateDbContextAsync(cancellationToken)
            .ConfigureAwait(false);

        var context = dbContext.Set<CronJobOccurrenceEntity<TCronJob>>();

        foreach (var item in cronJobOccurrences.Items)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (item.NextCronOccurrence is null)
            {
                var itemToAdd = new CronJobOccurrenceEntity<TCronJob>
                {
                    Id = Guid.NewGuid(),
                    Status = JobStatus.Queued,
                    LockHolder = LockHolder,
                    ExecutionTime = executionTime,
                    CronJobId = item.Id,
                    LockedAt = now,
                    CreatedAt = now,
                    UpdatedAt = now,
                };

                var affectAdded = await context
                    .Upsert(itemToAdd)
                    .On(x => new { x.ExecutionTime, x.CronJobId })
                    .NoUpdate()
                    .RunAsync(cancellationToken)
                    .ConfigureAwait(false);

                if (affectAdded <= 0)
                {
                    continue;
                }

                itemToAdd.CronJob = new TCronJob
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
                                .SetProperty(y => y.Status, JobStatus.Queued),
                        cancellationToken
                    )
                    .ConfigureAwait(false);

                if (affectedUpdate <= 0)
                {
                    continue;
                }

                yield return new CronJobOccurrenceEntity<TCronJob>
                {
                    Id = item.NextCronOccurrence.Id,
                    CronJobId = item.Id,
                    ExecutionTime = executionTime,
                    Status = JobStatus.Queued,
                    LockHolder = LockHolder,
                    LockedAt = now,
                    UpdatedAt = now,
                    CreatedAt = item.NextCronOccurrence.CreatedAt,
                    CronJob = new TCronJob
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

    public async Task<CronJobOccurrenceEntity<TCronJob>> GetEarliestAvailableCronOccurrence(
        Guid[] ids,
        CancellationToken cancellationToken = default
    )
    {
        var now = Clock.UtcNow;
        var mainSchedulerThreshold = now.AddSeconds(-1);
        await using var dbContext = await DbContextFactory
            .CreateDbContextAsync(cancellationToken)
            .ConfigureAwait(false);

        var occurrence = await dbContext
            .Set<CronJobOccurrenceEntity<TCronJob>>()
            .AsNoTracking()
            .Include(x => x.CronJob)
            .Where(x => ((IEnumerable<Guid>)ids).Contains(x.CronJobId))
            .Where(x => x.ExecutionTime >= mainSchedulerThreshold) // Only items within the 1-second main scheduler window
            .WhereCanAcquire(LockHolder)
            .OrderBy(x => x.ExecutionTime)
            .Select(
                MappingExtensions.ForLatestQueuedCronJobOccurrence<
                    CronJobOccurrenceEntity<TCronJob>,
                    TCronJob
                >()
            )
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);

        return occurrence!;
    }

    public async Task<byte[]> GetCronJobOccurrenceRequest(
        Guid jobId,
        CancellationToken cancellationToken = default
    )
    {
        await using var dbContext = await DbContextFactory
            .CreateDbContextAsync(cancellationToken)
            .ConfigureAwait(false);

        var request = await dbContext
            .Set<CronJobOccurrenceEntity<TCronJob>>()
            .AsNoTracking()
            .Include(x => x.CronJob)
            .Where(x => x.Id == jobId)
            .Select(x => x.CronJob.Request)
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);

        return request ?? Array.Empty<byte>();
    }

    public async Task UpdateCronJobOccurrencesWithUnifiedContext(
        Guid[] cronOccurrenceIds,
        InternalFunctionContext functionContext,
        CancellationToken cancellationToken = default
    )
    {
        await using var dbContext = await DbContextFactory
            .CreateDbContextAsync(cancellationToken)
            .ConfigureAwait(false);

        await dbContext
            .Set<CronJobOccurrenceEntity<TCronJob>>()
            .Where(x => ((IEnumerable<Guid>)cronOccurrenceIds).Contains(x.Id))
            .ExecuteUpdateAsync(setter => setter.UpdateCronJobOccurrence(functionContext), cancellationToken)
            .ConfigureAwait(false);
    }

    #endregion
}
