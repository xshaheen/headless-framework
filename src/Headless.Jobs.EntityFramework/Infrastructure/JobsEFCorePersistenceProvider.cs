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
    ILogger logger
)
    : BasePersistenceProvider<TDbContext, TTimeJob, TCronJob>(
        dbContextFactory,
        timeProvider,
        ownerIdentity,
        optionsBuilder,
        cache,
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
    Task ICoordinatedJobWriter<TTimeJob, TCronJob>.InvalidateCronExpressionsCacheAsync() =>
        InvalidateCronExpressionsCacheAsync();

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

    public async Task<TTimeJob?> GetTimeJobById(Guid id, CancellationToken cancellationToken = default)
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

    public async Task<TTimeJob[]> GetTimeJobs(
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

    public async Task<PaginationResult<TTimeJob>> GetTimeJobsPaginated(
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

    public async Task<int> AddTimeJobs(TTimeJob[] jobs, CancellationToken cancellationToken = default)
    {
        await using var dbContext = await DbContextFactory
            .CreateDbContextAsync(cancellationToken)
            .ConfigureAwait(false);

        await dbContext.Set<TTimeJob>().AddRangeAsync(jobs, cancellationToken).ConfigureAwait(false);

        return await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<int> UpdateTimeJobs(TTimeJob[] timeJobs, CancellationToken cancellationToken = default)
    {
        await using var dbContext = await DbContextFactory
            .CreateDbContextAsync(cancellationToken)
            .ConfigureAwait(false);

        dbContext.Set<TTimeJob>().UpdateRange(timeJobs);

        return await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<int> RemoveTimeJobs(Guid[] timeJobIds, CancellationToken cancellationToken = default)
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

    public async Task<TCronJob?> GetCronJobById(Guid id, CancellationToken cancellationToken)
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

    public async Task<TCronJob[]> GetCronJobs(
        Expression<Func<TCronJob, bool>>? predicate,
        CancellationToken cancellationToken
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

    public async Task<PaginationResult<TCronJob>> GetCronJobsPaginated(
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

    public async Task<int> InsertCronJobs(TCronJob[] jobs, CancellationToken cancellationToken)
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

    public async Task<int> UpdateCronJobs(TCronJob[] cronJobs, CancellationToken cancellationToken)
    {
        await using var dbContext = await DbContextFactory
            .CreateDbContextAsync(cancellationToken)
            .ConfigureAwait(false);

        dbContext.Set<TCronJob>().UpdateRange(cronJobs);

        var result = await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        await InvalidateCronExpressionsCacheAsync().ConfigureAwait(false);

        return result;
    }

    public async Task<int> RemoveCronJobs(Guid[] cronJobIds, CancellationToken cancellationToken)
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
    public async Task<CronJobOccurrenceEntity<TCronJob>[]> GetAllCronJobOccurrences(
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

    public async Task<PaginationResult<CronJobOccurrenceEntity<TCronJob>>> GetAllCronJobOccurrencesPaginated(
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

    public async Task<int> InsertCronJobOccurrences(
        CronJobOccurrenceEntity<TCronJob>[] cronJobOccurrences,
        CancellationToken cancellationToken
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

    public async Task<int> RemoveCronJobOccurrences(Guid[] cronJobOccurrences, CancellationToken cancellationToken)
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
        var now = TimeProvider.GetUtcNow().UtcDateTime;

        // Only acquire occurrences that are acquirable (Idle/Queued and not locked by another node)
        var query = dbContext
            .Set<CronJobOccurrenceEntity<TCronJob>>()
            .Where(x => ((IEnumerable<Guid>)occurrenceIds).Contains(x.Id))
            .WhereCanAcquire(owner, now);

        // Lock and mark InProgress
        var affected = await query
            .ExecuteUpdateAsync(
                setter =>
                    setter
                        .SetProperty(x => x.OwnerId, owner)
                        .SetProperty(x => x.LockedUntil, now.Add(LeaseDuration))
                        .SetProperty(x => x.Status, JobStatus.InProgress)
                        .SetProperty(x => x.UpdatedAt, now),
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
