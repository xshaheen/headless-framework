// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Linq.Expressions;
using Headless.Caching;
using Headless.CommitCoordination;
using Headless.Jobs.Entities;
using Headless.Jobs.Enums;
using Headless.Jobs.Interfaces;
using Headless.Jobs.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.Logging;

namespace Headless.Jobs.Infrastructure;

internal sealed class JobsEfCorePersistenceProvider<TDbContext, TTimeJob, TCronJob>(
    IDbContextFactory<TDbContext> dbContextFactory,
    DbContextOptions<TDbContext> coordinatedWriteOptions,
    TimeProvider timeProvider,
    IJobsOwnerIdentity ownerIdentity,
    SchedulerOptionsBuilder optionsBuilder,
    ICache? cache,
    IJobsClaimStrategy<TTimeJob, TCronJob> claimStrategy,
    ILogger logger
)
    : BasePersistenceProvider<TDbContext, TTimeJob, TCronJob>(
        dbContextFactory,
        timeProvider,
        ownerIdentity,
        optionsBuilder,
        cache,
        claimStrategy,
        logger
    ),
        IJobPersistenceProvider<TTimeJob, TCronJob>,
        ICoordinatedJobWriter<TTimeJob, TCronJob>
    where TDbContext : DbContext
    where TTimeJob : TimeJobEntity<TTimeJob>, new()
    where TCronJob : CronJobEntity, new()
{
    // The registered options template, cloned per coordinated write so the context attaches to the caller's
    // connection while reusing the cached compiled model / internal service provider — no model recompilation.

    // Compiled (DbContextOptions<TDbContext>) constructor delegate — the same constructor EF Core's DbContext pooling
    // requires, so any context usable with the pooled factory works here too. Cached per closed generic so coordinated
    // writes never pay reflection, and a context missing that constructor fails with a clear message instead of the
    // raw MissingMethodException Activator.CreateInstance would surface mid-transaction.
    private static readonly Func<DbContextOptions<TDbContext>, TDbContext> _CreateContext = _BuildContextFactory();

    private static Func<DbContextOptions<TDbContext>, TDbContext> _BuildContextFactory()
    {
        // Registration validates this constructor up front (see CoordinatedWriteContextFactory) so a misconfigured
        // context fails at DI-build with the direct message; this call is the defense-in-depth net for a provider
        // constructed outside that path.
        var constructor = CoordinatedWriteContextFactory.RequireOptionsConstructor<TDbContext>();

        var optionsParameter = Expression.Parameter(typeof(DbContextOptions<TDbContext>), "options");

        return Expression
            .Lambda<Func<DbContextOptions<TDbContext>, TDbContext>>(
                Expression.New(constructor, optionsParameter),
                optionsParameter
            )
            .Compile();
    }

    #region Coordinated_Write_Implementations

    async Task ICoordinatedJobWriter<TTimeJob, TCronJob>.WriteTimeJobsAsync(
        TTimeJob[] jobs,
        IRelationalCommitContext relationalContext,
        CancellationToken cancellationToken
    )
    {
        await using var dbContext = _CreateCoordinatedContext(relationalContext);
        await dbContext.Set<TTimeJob>().AddRangeAsync(jobs, cancellationToken).ConfigureAwait(false);
        await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    async Task ICoordinatedJobWriter<TTimeJob, TCronJob>.WriteCronJobsAsync(
        TCronJob[] jobs,
        IRelationalCommitContext relationalContext,
        CancellationToken cancellationToken
    )
    {
        await using var dbContext = _CreateCoordinatedContext(relationalContext);
        await dbContext.Set<TCronJob>().AddRangeAsync(jobs, cancellationToken).ConfigureAwait(false);
        await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    // The cron-expressions cache is owned by the base provider (it holds the ICache + key); the manager registers
    // this on OnCommit so the coordinated cron path invalidates only after the caller's transaction commits.
    Task ICoordinatedJobWriter<TTimeJob, TCronJob>.InvalidateCronExpressionsCacheAsync()
    {
        return InvalidateCronExpressionsCacheAsync();
    }

    // Builds a short-lived, NON-pooled context bound to the caller's already-open connection + live transaction.
    // The pooled factory cannot be reused: a pooled context owns its own connection and Database.UseTransaction
    // requires the transaction's connection to be the context's current connection (KTD-1). Cloning the registered
    // options template and swapping only the relational connection keeps the compiled model cached (the model cache
    // key is unchanged) and preserves the schema/model customizer. WithConnection(connection, owned: false) clears
    // the template's connection string (EF asserts ConnectionString is null once a Connection is set) and marks the
    // connection unowned so EF never disposes or closes the caller's connection.
    private TDbContext _CreateCoordinatedContext(IRelationalCommitContext relationalContext)
    {
        var connection =
            relationalContext.Connection
            ?? throw new InvalidOperationException(
                "The relational commit context exposed no live connection for the coordinated job write."
            );

        var transaction =
            relationalContext.Transaction
            ?? throw new InvalidOperationException(
                "The relational commit context exposed no live transaction for the coordinated job write."
            );

        var reboundRelational = RelationalOptionsExtension
            .Extract(coordinatedWriteOptions)
            .WithConnection(connection, owned: false);

        var coordinatedOptionsBuilder = new DbContextOptionsBuilder<TDbContext>(coordinatedWriteOptions);
        ((IDbContextOptionsBuilderInfrastructure)coordinatedOptionsBuilder).AddOrUpdateExtension(reboundRelational);

        var dbContext = _CreateContext(coordinatedOptionsBuilder.Options);
#pragma warning disable MA0045 // Enlisting an existing transaction is an in-memory operation (no I/O), and this is a synchronous context factory.
        dbContext.Database.UseTransaction(transaction);
#pragma warning restore MA0045

        return dbContext;
    }

    #endregion

    #region Time_Ticker_Implementations

    public async Task<TTimeJob?> GetTimeJobByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        await using var dbContext = await DbContextFactory
            .CreateDbContextAsync(cancellationToken)
            .ConfigureAwait(false);

        return await dbContext
            .Set<TTimeJob>()
            .AsNoTracking()
            .Include(x => x.Children)
            .FirstOrDefaultAsync(x => x.Id == id, cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<TTimeJob[]> GetTimeJobsAsync(
        Expression<Func<TTimeJob, bool>>? predicate,
        CancellationToken cancellationToken = default
    )
    {
        await using var dbContext = await DbContextFactory
            .CreateDbContextAsync(cancellationToken)
            .ConfigureAwait(false);

        var baseQuery = dbContext.Set<TTimeJob>().Include(x => x.Children).ThenInclude(x => x.Children).AsNoTracking();

        if (predicate != null)
        {
            baseQuery = baseQuery.Where(predicate);
        }

        return await baseQuery
            .Where(x => x.ParentId == null)
            .OrderByDescending(x => x.ExecutionTime)
            .ToArrayAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<PaginationResult<TTimeJob>> GetTimeJobsPaginatedAsync(
        Expression<Func<TTimeJob, bool>>? predicate,
        int pageNumber,
        int pageSize,
        CancellationToken cancellationToken = default
    )
    {
        await using var dbContext = await DbContextFactory
            .CreateDbContextAsync(cancellationToken)
            .ConfigureAwait(false);

        var baseQuery = dbContext.Set<TTimeJob>().Include(x => x.Children).ThenInclude(x => x.Children).AsNoTracking();

        if (predicate != null)
        {
            baseQuery = baseQuery.Where(predicate);
        }

        baseQuery = baseQuery.Where(x => x.ParentId == null).OrderByDescending(x => x.ExecutionTime);

        return await baseQuery.ToPaginatedListAsync(pageNumber, pageSize, cancellationToken).ConfigureAwait(false);
    }

    public async Task<int> AddTimeJobsAsync(TTimeJob[] jobs, CancellationToken cancellationToken = default)
    {
        await using var dbContext = await DbContextFactory
            .CreateDbContextAsync(cancellationToken)
            .ConfigureAwait(false);

        await dbContext.Set<TTimeJob>().AddRangeAsync(jobs, cancellationToken).ConfigureAwait(false);

        return await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<int> UpdateTimeJobsAsync(TTimeJob[] timeJobs, CancellationToken cancellationToken = default)
    {
        await using var dbContext = await DbContextFactory
            .CreateDbContextAsync(cancellationToken)
            .ConfigureAwait(false);

        dbContext.Set<TTimeJob>().UpdateRange(timeJobs);

        return await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<int> RemoveTimeJobsAsync(Guid[] timeJobIds, CancellationToken cancellationToken = default)
    {
        await using var dbContext = await DbContextFactory
            .CreateDbContextAsync(cancellationToken)
            .ConfigureAwait(false);

        // Load the entities to be deleted (including children for cascade delete)
        var tickersToDelete = await dbContext
            .Set<TTimeJob>()
            .Include(x => x.Children)
                .ThenInclude(x => x.Children) // Include grandchildren if needed
            .Where(x => timeJobIds.Contains(x.Id))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        // Remove using Entity Framework (respects cascade delete configuration)
        dbContext.Set<TTimeJob>().RemoveRange(tickersToDelete);

        return await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }
    #endregion

    #region Cron_Ticker_Implementations

    public async Task<TCronJob?> GetCronJobByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        await using var dbContext = await DbContextFactory
            .CreateDbContextAsync(cancellationToken)
            .ConfigureAwait(false);

        return await dbContext
            .Set<TCronJob>()
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == id, cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<TCronJob?> PauseCronJobAsync(
        Guid cronJobId,
        DateTime operationTimeUtc,
        CancellationToken cancellationToken = default
    )
    {
        await using var dbContext = await DbContextFactory
            .CreateDbContextAsync(cancellationToken)
            .ConfigureAwait(false);
        await using var transaction = await dbContext
            .Database.BeginTransactionAsync(cancellationToken)
            .ConfigureAwait(false);

        var accepted = await dbContext
            .Set<TCronJob>()
            .Where(x => x.Id == cronJobId && !x.IsPaused)
            .ExecuteUpdateAsync(
                setter =>
                    setter
                        .SetProperty(x => x.IsPaused, valueExpression: true)
                        .SetProperty(x => x.ScheduleRevision, x => x.ScheduleRevision + 1)
                        .SetProperty(x => x.UpdatedAt, operationTimeUtc),
                cancellationToken
            )
            .ConfigureAwait(false);

        if (accepted == 0)
        {
            return null;
        }

        await dbContext
            .Set<CronJobOccurrenceEntity<TCronJob>>()
            .Where(x => x.CronJobId == cronJobId && (x.Status == JobStatus.Idle || x.Status == JobStatus.Queued))
            .ExecuteUpdateAsync(
                setter =>
                    setter
                        .SetProperty(x => x.Status, JobStatus.Skipped)
                        .SetProperty(x => x.ExecutedAt, operationTimeUtc)
                        .SetProperty(x => x.UpdatedAt, operationTimeUtc)
                        .SetProperty(x => x.SkippedReason, "Cron definition paused")
                        .SetProperty(x => x.OwnerId, _ => null)
                        .SetProperty(x => x.LockedUntil, _ => null),
                cancellationToken
            )
            .ConfigureAwait(false);

        var result = await dbContext
            .Set<TCronJob>()
            .AsNoTracking()
            .SingleAsync(x => x.Id == cronJobId, cancellationToken)
            .ConfigureAwait(false);
        await transaction.CommitAsync(CancellationToken.None).ConfigureAwait(false);
        await InvalidateCronExpressionsCacheAsync().ConfigureAwait(false);

        return result;
    }

    public async Task<TCronJob?> ResumeCronJobAsync(
        Guid cronJobId,
        long expectedScheduleRevision,
        CronJobOccurrenceEntity<TCronJob> nextOccurrence,
        DateTime operationTimeUtc,
        CancellationToken cancellationToken = default
    )
    {
        if (nextOccurrence.CronJobId != cronJobId)
        {
            return null;
        }

        await using var dbContext = await DbContextFactory
            .CreateDbContextAsync(cancellationToken)
            .ConfigureAwait(false);
        await using var transaction = await dbContext
            .Database.BeginTransactionAsync(cancellationToken)
            .ConfigureAwait(false);

        var accepted = await dbContext
            .Set<TCronJob>()
            .Where(x => x.Id == cronJobId && x.IsPaused && x.ScheduleRevision == expectedScheduleRevision)
            .ExecuteUpdateAsync(
                setter =>
                    setter
                        .SetProperty(x => x.IsPaused, valueExpression: false)
                        .SetProperty(x => x.ScheduleRevision, x => x.ScheduleRevision + 1)
                        .SetProperty(x => x.UpdatedAt, operationTimeUtc),
                cancellationToken
            )
            .ConfigureAwait(false);

        if (accepted == 0)
        {
            return null;
        }

        nextOccurrence.CronJob = null!;
        await dbContext.Set<CronJobOccurrenceEntity<TCronJob>>().AddAsync(nextOccurrence, cancellationToken);
        await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        var result = await dbContext
            .Set<TCronJob>()
            .AsNoTracking()
            .SingleAsync(x => x.Id == cronJobId, cancellationToken)
            .ConfigureAwait(false);
        await transaction.CommitAsync(CancellationToken.None).ConfigureAwait(false);
        await InvalidateCronExpressionsCacheAsync().ConfigureAwait(false);

        return result;
    }

    public async Task<TCronJob[]?> UpdateCronJobsAtomicallyAsync(
        CronJobAtomicUpdate<TCronJob>[] updates,
        DateTime operationTimeUtc,
        CancellationToken cancellationToken = default
    )
    {
        if (updates.Select(x => x.Definition.Id).ToHashSet().Count != updates.Length)
        {
            return null;
        }

        await using var dbContext = await DbContextFactory
            .CreateDbContextAsync(cancellationToken)
            .ConfigureAwait(false);
        await using var transaction = await dbContext
            .Database.BeginTransactionAsync(cancellationToken)
            .ConfigureAwait(false);
        var definitionIds = updates.Select(x => x.Definition.Id).ToArray();
        var currentById = await dbContext
            .Set<TCronJob>()
            .AsNoTracking()
            .Where(x => definitionIds.Contains(x.Id))
            .ToDictionaryAsync(x => x.Id, cancellationToken)
            .ConfigureAwait(false);
        var results = new TCronJob[updates.Length];

        foreach (
            var (update, inputIndex) in updates
                .Select((update, index) => (update, index))
                .OrderBy(x => x.update.Definition.Id)
        )
        {
            if (
                !currentById.TryGetValue(update.Definition.Id, out var current)
                || current.ScheduleRevision != update.ExpectedScheduleRevision
            )
            {
                return null;
            }

            var scheduleChanged =
                !string.Equals(current.Expression, update.Definition.Expression, StringComparison.Ordinal)
                || !string.Equals(current.TimeZoneId, update.Definition.TimeZoneId, StringComparison.Ordinal);

            if (scheduleChanged && !current.IsPaused && update.NextOccurrence is null)
            {
                return null;
            }

            var affected = await dbContext
                .Set<TCronJob>()
                .Where(x => x.Id == current.Id && x.ScheduleRevision == update.ExpectedScheduleRevision)
                .ExecuteUpdateAsync(
                    setter =>
                        setter
                            .SetProperty(x => x.Function, update.Definition.Function)
                            .SetProperty(x => x.Description, update.Definition.Description)
                            .SetProperty(x => x.Expression, update.Definition.Expression)
                            .SetProperty(x => x.TimeZoneId, update.Definition.TimeZoneId)
                            .SetProperty(x => x.Request, update.Definition.Request)
                            .SetProperty(x => x.Retries, update.Definition.Retries)
                            .SetProperty(x => x.RetryIntervals, update.Definition.RetryIntervals)
                            .SetProperty(x => x.OnNodeDeath, update.Definition.OnNodeDeath)
                            .SetProperty(
                                x => x.ScheduleRevision,
                                scheduleChanged ? current.ScheduleRevision + 1 : current.ScheduleRevision
                            )
                            .SetProperty(x => x.UpdatedAt, operationTimeUtc),
                    cancellationToken
                )
                .ConfigureAwait(false);

            if (affected == 0)
            {
                return null;
            }

            if (scheduleChanged)
            {
                await dbContext
                    .Set<CronJobOccurrenceEntity<TCronJob>>()
                    .Where(x =>
                        x.CronJobId == current.Id && (x.Status == JobStatus.Idle || x.Status == JobStatus.Queued)
                    )
                    .ExecuteUpdateAsync(
                        setter =>
                            setter
                                .SetProperty(x => x.Status, JobStatus.Skipped)
                                .SetProperty(x => x.ExecutedAt, operationTimeUtc)
                                .SetProperty(x => x.UpdatedAt, operationTimeUtc)
                                .SetProperty(x => x.SkippedReason, "Cron definition updated")
                                .SetProperty(x => x.OwnerId, _ => null)
                                .SetProperty(x => x.LockedUntil, _ => null),
                        cancellationToken
                    )
                    .ConfigureAwait(false);

                if (!current.IsPaused)
                {
                    update.NextOccurrence!.CronJobId = current.Id;
                    update.NextOccurrence.CronJob = null!;
                    await dbContext
                        .Set<CronJobOccurrenceEntity<TCronJob>>()
                        .AddAsync(update.NextOccurrence, cancellationToken)
                        .ConfigureAwait(false);
                }
            }

            var result = update.Definition;
            result.IsPaused = current.IsPaused;
            result.ScheduleRevision = scheduleChanged ? current.ScheduleRevision + 1 : current.ScheduleRevision;
            result.CreatedAt = current.CreatedAt;
            result.UpdatedAt = operationTimeUtc;
            results[inputIndex] = result;
        }

        await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        await transaction.CommitAsync(CancellationToken.None).ConfigureAwait(false);
        await InvalidateCronExpressionsCacheAsync().ConfigureAwait(false);

        return results;
    }

    public async Task<TCronJob[]> GetCronJobsAsync(
        Expression<Func<TCronJob, bool>>? predicate,
        CancellationToken cancellationToken = default
    )
    {
        await using var dbContext = await DbContextFactory
            .CreateDbContextAsync(cancellationToken)
            .ConfigureAwait(false);

        var baseQuery = dbContext.Set<TCronJob>().AsNoTracking();

        if (predicate != null)
        {
            baseQuery = baseQuery.Where(predicate);
        }

        return await baseQuery
            .OrderByDescending(x => x.CreatedAt)
            .ToArrayAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<PaginationResult<TCronJob>> GetCronJobsPaginatedAsync(
        Expression<Func<TCronJob, bool>>? predicate,
        int pageNumber,
        int pageSize,
        CancellationToken cancellationToken = default
    )
    {
        await using var dbContext = await DbContextFactory
            .CreateDbContextAsync(cancellationToken)
            .ConfigureAwait(false);

        var baseQuery = dbContext.Set<TCronJob>().AsNoTracking();

        if (predicate != null)
        {
            baseQuery = baseQuery.Where(predicate);
        }

        baseQuery = baseQuery.OrderByDescending(x => x.CreatedAt);

        return await baseQuery.ToPaginatedListAsync(pageNumber, pageSize, cancellationToken).ConfigureAwait(false);
    }

    public async Task<int> InsertCronJobsAsync(TCronJob[] jobs, CancellationToken cancellationToken = default)
    {
        await using var dbContext = await DbContextFactory
            .CreateDbContextAsync(cancellationToken)
            .ConfigureAwait(false);

        await dbContext.Set<TCronJob>().AddRangeAsync(jobs, cancellationToken).ConfigureAwait(false);

        var result = await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        // Direct (non-coordinated) cron enqueue owns its cache invalidation here, post-SaveChanges. The coordinated
        // enqueue path is a pure row write (WriteCronJobsAsync) and invalidates from the manager post-commit instead
        // — see JobsManager._RunCoordinatedCronJob(s)(Batch)SideEffectsAsync. Keep both sites in sync.
        await InvalidateCronExpressionsCacheAsync().ConfigureAwait(false);

        return result;
    }

    public async Task<int> UpdateCronJobsAsync(TCronJob[] cronJobs, CancellationToken cancellationToken = default)
    {
        await using var dbContext = await DbContextFactory
            .CreateDbContextAsync(cancellationToken)
            .ConfigureAwait(false);

        dbContext.Set<TCronJob>().UpdateRange(cronJobs);

        var result = await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        await InvalidateCronExpressionsCacheAsync().ConfigureAwait(false);

        return result;
    }

    public async Task<int> RemoveCronJobsAsync(Guid[] cronJobIds, CancellationToken cancellationToken = default)
    {
        await using var dbContext = await DbContextFactory
            .CreateDbContextAsync(cancellationToken)
            .ConfigureAwait(false);
        var result = await dbContext
            .Set<TCronJob>()
            .Where(x => ((IEnumerable<Guid>)cronJobIds).Contains(x.Id))
            .ExecuteDeleteAsync(cancellationToken)
            .ConfigureAwait(false);

        await InvalidateCronExpressionsCacheAsync().ConfigureAwait(false);

        return result;
    }

    #endregion

    #region Cron_TickerOccurrence_Implementations
    public async Task<CronJobOccurrenceEntity<TCronJob>[]> GetAllCronJobOccurrencesAsync(
        Expression<Func<CronJobOccurrenceEntity<TCronJob>, bool>>? predicate,
        CancellationToken cancellationToken = default
    )
    {
        await using var dbContext = await DbContextFactory
            .CreateDbContextAsync(cancellationToken)
            .ConfigureAwait(false);
        var cronJobOccurrenceContext = dbContext.Set<CronJobOccurrenceEntity<TCronJob>>().AsNoTracking();

        var query =
            predicate == null
                ? cronJobOccurrenceContext.Include(x => x.CronJob)
                : cronJobOccurrenceContext.Include(x => x.CronJob).Where(predicate);

        return await query
            .OrderByDescending(x => x.ExecutionTime)
            .ToArrayAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<CronOccurrenceStatusCount[]> GetCronOccurrenceGraphStatusCountsAsync(
        Guid cronJobId,
        DateTime today,
        CancellationToken cancellationToken = default
    )
    {
        await using var dbContext = await DbContextFactory
            .CreateDbContextAsync(cancellationToken)
            .ConfigureAwait(false);
        var occurrences = dbContext.Set<CronJobOccurrenceEntity<TCronJob>>().AsNoTracking();

        var occurrenceDates = await occurrences
            .Where(x => x.CronJobId == cronJobId)
            .Select(x => x.ExecutionTime.Date)
            .Distinct()
            .ToArrayAsync(cancellationToken)
            .ConfigureAwait(false);
        var range = CronOccurrenceGraphRangeSelector.Select(occurrenceDates, today);
        var exclusiveEnd = range.EndDate.AddDays(1);

        var aggregateRows = await occurrences
            .Where(x => x.CronJobId == cronJobId)
            .Where(x => x.ExecutionTime >= range.StartDate && x.ExecutionTime < exclusiveEnd)
            .GroupBy(x => new { x.ExecutionTime.Date, x.Status })
            .Select(group => new
            {
                group.Key.Date,
                group.Key.Status,
                Count = group.Count(),
            })
            .ToArrayAsync(cancellationToken)
            .ConfigureAwait(false);

        var counts = aggregateRows.Select(x => new CronOccurrenceStatusCount
        {
            Date = x.Date,
            Status = x.Status,
            Count = x.Count,
        });

        return CronOccurrenceGraphRangeSelector.AddRangeBoundaries(counts, range);
    }

    public async Task<PaginationResult<CronJobOccurrenceEntity<TCronJob>>> GetAllCronJobOccurrencesPaginatedAsync(
        Expression<Func<CronJobOccurrenceEntity<TCronJob>, bool>>? predicate,
        int pageNumber,
        int pageSize,
        CancellationToken cancellationToken = default
    )
    {
        await using var dbContext = await DbContextFactory
            .CreateDbContextAsync(cancellationToken)
            .ConfigureAwait(false);

        var baseQuery = dbContext.Set<CronJobOccurrenceEntity<TCronJob>>().Include(x => x.CronJob).AsNoTracking();

        if (predicate != null)
        {
            baseQuery = baseQuery.Where(predicate);
        }

        baseQuery = baseQuery.OrderByDescending(x => x.ExecutionTime);

        return await baseQuery.ToPaginatedListAsync(pageNumber, pageSize, cancellationToken).ConfigureAwait(false);
    }

    public async Task<int> InsertCronJobOccurrencesAsync(
        CronJobOccurrenceEntity<TCronJob>[] cronJobOccurrences,
        CancellationToken cancellationToken = default
    )
    {
        await using var dbContext = await DbContextFactory
            .CreateDbContextAsync(cancellationToken)
            .ConfigureAwait(false);

        await dbContext
            .Set<CronJobOccurrenceEntity<TCronJob>>()
            .AddRangeAsync(cronJobOccurrences, cancellationToken)
            .ConfigureAwait(false);

        return await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<int> RemoveCronJobOccurrencesAsync(
        Guid[] cronJobOccurrences,
        CancellationToken cancellationToken = default
    )
    {
        await using var dbContext = await DbContextFactory
            .CreateDbContextAsync(cancellationToken)
            .ConfigureAwait(false);
        return await dbContext
            .Set<CronJobOccurrenceEntity<TCronJob>>()
            .Where(x => ((IEnumerable<Guid>)cronJobOccurrences).Contains(x.Id))
            .ExecuteDeleteAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<CronJobOccurrenceEntity<TCronJob>[]> AcquireImmediateCronOccurrencesAsync(
        Guid[]? occurrenceIds,
        CancellationToken cancellationToken = default
    )
    {
        if (occurrenceIds == null || occurrenceIds.Length == 0 || !OwnerIdentity.TryGetStampOwner(out var owner))
        {
            return [];
        }

        await using var dbContext = await DbContextFactory
            .CreateDbContextAsync(cancellationToken)
            .ConfigureAwait(false);
        // Only acquire occurrences that are acquirable (Idle/Queued and not locked by another node)
        var query = dbContext
            .Set<CronJobOccurrenceEntity<TCronJob>>()
            .Where(x => ((IEnumerable<Guid>)occurrenceIds).Contains(x.Id))
            .WhereCanAcquireUsingDatabaseClock(owner);

        // Lock and mark InProgress
        var affected = await query
            .ExecuteUpdateAsync(
                setter =>
                    setter
                        .SetProperty(x => x.OwnerId, owner)
                        .SetProperty(x => x.LockedUntil, _ => DateTime.UtcNow.AddSeconds(LeaseDuration.TotalSeconds))
                        .SetProperty(x => x.Status, JobStatus.InProgress)
                        .SetProperty(x => x.UpdatedAt, _ => DateTime.UtcNow),
                cancellationToken
            )
            .ConfigureAwait(false);

        if (affected == 0)
        {
            return [];
        }

        // Return acquired occurrences with CronJob populated
        return await dbContext
            .Set<CronJobOccurrenceEntity<TCronJob>>()
            .AsNoTracking()
            .Where(x =>
                ((IEnumerable<Guid>)occurrenceIds).Contains(x.Id)
                && x.OwnerId == owner
                && x.Status == JobStatus.InProgress
            )
            .Include(x => x.CronJob)
            .ToArrayAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    #endregion
}
