// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Linq.Expressions;
using Headless.Abstractions;
using Headless.Caching;
using Headless.Checks;
using Headless.CommitCoordination;
using Headless.Jobs.Entities;
using Headless.Jobs.Enums;
using Headless.Jobs.Interfaces;
using Headless.Jobs.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.Logging;

#pragma warning disable MA0133 // EF must keep DateTime.UtcNow in expression trees so providers translate the database clock before the DateTimeOffset assignment.
namespace Headless.Jobs.Infrastructure;

internal sealed class JobsEfCorePersistenceProvider<TDbContext, TTimeJob, TCronJob>(
    IDbContextFactory<TDbContext> dbContextFactory,
    DbContextOptions<TDbContext> coordinatedWriteOptions,
    TimeProvider timeProvider,
    IGuidGenerator guidGenerator,
    IJobsOwnerIdentity ownerIdentity,
    SchedulerOptionsBuilder optionsBuilder,
    ICache? cache,
    IJobsClaimStrategy<TTimeJob, TCronJob> claimStrategy,
    ILogger logger
)
    : BasePersistenceProvider<TDbContext, TTimeJob, TCronJob>(
        dbContextFactory,
        timeProvider,
        guidGenerator,
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

    async Task<CronSchedulePositionSeedResult> ICoordinatedJobWriter<TTimeJob, TCronJob>.WriteCronJobsAsync(
        TCronJob[] jobs,
        CronSchedulePositionSeeder seeder,
        IRelationalCommitContext relationalContext,
        CancellationToken cancellationToken
    )
    {
        await using var dbContext = _CreateCoordinatedContext(relationalContext);

        // The caller's transaction may have opened long before this call, which is exactly why the anchor is the
        // STATEMENT clock: PostgreSQL's now() would report that transaction's start and position the definition
        // before it existed. The seed is still bounded by commit time — a caller that holds its transaction open for
        // minutes after enqueuing seeds a slightly stale position — but that direction only produces a small backlog
        // for the missed-run policy to resolve, never a silently skipped tick.
        var storeUtcNow = await JobsStoreClock
            .GetStatementUtcNowAsync(dbContext, cancellationToken)
            .ConfigureAwait(false);

        var earliestNextDueUtc = _ApplySchedulePositionSeed(jobs, seeder, storeUtcNow);

        await dbContext.Set<TCronJob>().AddRangeAsync(jobs, cancellationToken).ConfigureAwait(false);
        var affected = await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return new CronSchedulePositionSeedResult
        {
            StoreUtcNow = storeUtcNow,
            AffectedRows = affected,
            EarliestNextDueUtc = earliestNextDueUtc,
        };
    }

    // Stamps each definition with the position the seeder derives from the store's anchor and reports the earliest one
    // written, so the caller arms its scheduler restart from persisted state instead of a node-clock projection.
    private static DateTime? _ApplySchedulePositionSeed(
        TCronJob[] jobs,
        CronSchedulePositionSeeder seeder,
        DateTime storeUtcNow
    )
    {
        DateTime? earliestNextDueUtc = null;

        foreach (var job in jobs)
        {
            var seed = seeder(job, storeUtcNow);
            job.ReconciledThroughUtc = seed.ReconciledThroughUtc;
            job.NextDueUtc = seed.NextDueUtc;
            job.EvaluationFingerprint = seed.EvaluationFingerprint;

            if (earliestNextDueUtc is null || seed.NextDueUtc < earliestNextDueUtc.Value)
            {
                earliestNextDueUtc = seed.NextDueUtc;
            }
        }

        return earliestNextDueUtc;
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

        // Ownership and business lineage are captured at scheduling; ordinary edits cannot replace them.
        foreach (var entry in dbContext.ChangeTracker.Entries<TTimeJob>())
        {
            entry.Property(nameof(Entities.BaseEntity.BaseJobEntity.TenantId)).IsModified = false;
            entry.Property(nameof(Entities.BaseEntity.BaseJobEntity.CorrelationId)).IsModified = false;
            entry.Property(nameof(Entities.BaseEntity.BaseJobEntity.CausationId)).IsModified = false;
        }

        return await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<int> RemoveTimeJobsAsync(Guid[] timeJobIds, CancellationToken cancellationToken = default)
    {
        if (timeJobIds.Length == 0)
        {
            return 0;
        }

        await using var dbContext = await DbContextFactory
            .CreateDbContextAsync(cancellationToken)
            .ConfigureAwait(false);
        // The Parent/Children FK is DeleteBehavior.NoAction (TimeJobConfigurations): neither EF nor the database
        // cascades, so the subtree must be resolved explicitly. A surviving descendant is never harmless — a
        // non-timed one is unreachable forever (every claim path requires ExecutionTime != null), and a timed one
        // whose ParentId was nulled passes the ParentId == null arm of the parent-terminal gate and runs
        // unconditionally at its scheduled time. Walked one level at a time rather than with a recursive CTE so a
        // single query shape serves every relational provider; the visited set also terminates a corrupted cycle.
        var levels = new List<Guid[]> { timeJobIds };
        var visited = new HashSet<Guid>(timeJobIds);
        var frontier = timeJobIds;

        while (frontier.Length > 0)
        {
            var parentIds = frontier;

            var childIds = await dbContext
                .Set<TTimeJob>()
                .AsNoTracking()
                .Where(x => x.ParentId != null && ((IEnumerable<Guid>)parentIds).Contains(x.ParentId.Value))
                .Select(x => x.Id)
                .ToArrayAsync(cancellationToken)
                .ConfigureAwait(false);

            frontier = [.. childIds.Where(visited.Add)];

            if (frontier.Length > 0)
            {
                levels.Add(frontier);
            }
        }

        await using var transaction = await dbContext
            .Database.BeginTransactionAsync(cancellationToken)
            .ConfigureAwait(false);

        // Deepest level first: with a non-cascading FK a row may only be deleted once its children are gone.
        var deleted = 0;
        for (var level = levels.Count - 1; level >= 0; level--)
        {
            var ids = levels[level];

            deleted += await dbContext
                .Set<TTimeJob>()
                .Where(x => ((IEnumerable<Guid>)ids).Contains(x.Id))
                .ExecuteDeleteAsync(cancellationToken)
                .ConfigureAwait(false);
        }

        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);

        return deleted;
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
        DateTimeOffset operationTimeUtc,
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
                        // A paused definition must not fire; resume creates its own occurrence. Nothing is owed at
                        // this instant, so the retired row accounts for it.
                        .SetProperty(x => x.Disposition, CronOccurrenceDisposition.Accounted)
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
        Func<DateTime, CronJobOccurrenceEntity<TCronJob>?> nextOccurrenceFactory,
        DateTimeOffset operationTimeUtc,
        CancellationToken cancellationToken = default
    )
    {
        await using var dbContext = await DbContextFactory
            .CreateDbContextAsync(cancellationToken)
            .ConfigureAwait(false);
        await using var transaction = await dbContext
            .Database.BeginTransactionAsync(cancellationToken)
            .ConfigureAwait(false);

        // R10: the schedule position moves inside the SAME transition that clears the pause and bumps the revision, so
        // no window exposes a resumed definition still carrying its pre-pause position — which would read as a backlog
        // spanning the entire pause and hand recovery an interval that was deliberately not running.
        //
        var accepted = await dbContext
            .Set<TCronJob>()
            .Where(x => x.Id == cronJobId && x.IsPaused && x.ScheduleRevision == expectedScheduleRevision)
            .ExecuteUpdateAsync(
                setter =>
                    setter
                        .SetProperty(x => x.IsPaused, valueExpression: false)
                        .SetProperty(x => x.ScheduleRevision, x => x.ScheduleRevision + 1)
                        .SetProperty(x => x.ReconciledThroughUtc, _ => DateTime.UtcNow)
                        .SetProperty(x => x.UpdatedAt, operationTimeUtc),
                cancellationToken
            )
            .ConfigureAwait(false);

        if (accepted == 0)
        {
            return null;
        }

        var scheduleAnchorUtc = await dbContext
            .Set<TCronJob>()
            .AsNoTracking()
            .Where(x => x.Id == cronJobId)
            .Select(x => x.ReconciledThroughUtc)
            .SingleAsync(cancellationToken)
            .ConfigureAwait(false);
        scheduleAnchorUtc = DateTime.SpecifyKind(scheduleAnchorUtc, DateTimeKind.Utc);
        var nextOccurrence = nextOccurrenceFactory(scheduleAnchorUtc);
        if (nextOccurrence is null || nextOccurrence.CronJobId != cronJobId)
        {
            return null;
        }

        var resumeProjection = nextOccurrence.ExecutionTime;
        var evaluationFingerprint = nextOccurrence.CronJob?.EvaluationFingerprint;
        await dbContext
            .Set<TCronJob>()
            .Where(x => x.Id == cronJobId)
            .ExecuteUpdateAsync(
                setter =>
                    setter
                        .SetProperty(x => x.NextDueUtc, resumeProjection)
                        .SetProperty(x => x.EvaluationFingerprint, evaluationFingerprint)
                        .SetProperty(x => x.FingerprintFailureCount, 0)
                        .SetProperty(x => x.FingerprintRetryAfterUtc, _ => null),
                cancellationToken
            )
            .ConfigureAwait(false);

        var contractDefinition = await dbContext
            .Set<TCronJob>()
            .AsNoTracking()
            .SingleAsync(x => x.Id == cronJobId, cancellationToken)
            .ConfigureAwait(false);
        nextOccurrence.SnapshotContract(contractDefinition);
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
        DateTimeOffset operationTimeUtc,
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
            var recoveryChanged =
                current.OnMissedRun != update.Definition.OnMissedRun
                || current.MissedRunGraceSeconds != update.Definition.MissedRunGraceSeconds;
            var revisionChanged = scheduleChanged || recoveryChanged;

            if (scheduleChanged && !current.IsPaused && update.NextOccurrenceFactory is null)
            {
                return null;
            }

            // R10: a schedule-changing edit rebases the position in the same transition that bumps the revision, so the
            // old expression's projection never survives the edit. A metadata-only edit leaves both untouched — the
            // schedule did not move, so neither should the position. The provider stamps its own clock first, then
            // supplies that exact persisted anchor to the occurrence factory before this transaction commits.
            var rebasePosition = scheduleChanged && !current.IsPaused;

            var affected = await dbContext
                .Set<TCronJob>()
                .Where(x => x.Id == current.Id && x.ScheduleRevision == update.ExpectedScheduleRevision)
                .ExecuteUpdateAsync(
                    setter =>
                        setter
                            .SetProperty(x => x.Function, update.Definition.Function)
                            .SetProperty(x => x.ContractVersion, update.Definition.ContractVersion)
                            .SetProperty(x => x.Description, update.Definition.Description)
                            .SetProperty(x => x.Expression, update.Definition.Expression)
                            .SetProperty(x => x.TimeZoneId, update.Definition.TimeZoneId)
                            .SetProperty(x => x.Request, update.Definition.Request)
                            .SetProperty(x => x.Retries, update.Definition.Retries)
                            .SetProperty(x => x.RetryIntervals, update.Definition.RetryIntervals)
                            .SetProperty(x => x.OnNodeDeath, update.Definition.OnNodeDeath)
                            // R17: the runtime API is the AUTHORITY for these two. The attribute only seeds them at
                            // creation and is never reapplied, so persisting them here is what makes an operator
                            // override survive restarts. They change recovery semantics and therefore bump the same
                            // revision fence used by recovery, without replacing the schedule occurrence.
                            .SetProperty(x => x.OnMissedRun, update.Definition.OnMissedRun)
                            .SetProperty(x => x.MissedRunGraceSeconds, update.Definition.MissedRunGraceSeconds)
                            .SetProperty(
                                x => x.ScheduleRevision,
                                revisionChanged ? current.ScheduleRevision + 1 : current.ScheduleRevision
                            )
                            .SetProperty(
                                x => x.EvaluationFingerprint,
                                x =>
                                    revisionChanged
                                        ? update.Definition.EvaluationFingerprint ?? x.EvaluationFingerprint
                                        : x.EvaluationFingerprint
                            )
                            .SetProperty(
                                x => x.FingerprintFailureCount,
                                x => revisionChanged ? 0 : x.FingerprintFailureCount
                            )
                            .SetProperty(
                                x => x.FingerprintRetryAfterUtc,
                                x => revisionChanged ? null : x.FingerprintRetryAfterUtc
                            )
                            .SetProperty(
                                x => x.ReconciledThroughUtc,
                                x => rebasePosition ? DateTime.UtcNow : x.ReconciledThroughUtc
                            )
                            .SetProperty(x => x.UpdatedAt, operationTimeUtc),
                    cancellationToken
                )
                .ConfigureAwait(false);

            if (affected == 0)
            {
                return null;
            }

            CronJobOccurrenceEntity<TCronJob>? replacement = null;
            if (rebasePosition)
            {
                var scheduleAnchorUtc = await dbContext
                    .Set<TCronJob>()
                    .AsNoTracking()
                    .Where(x => x.Id == current.Id)
                    .Select(x => x.ReconciledThroughUtc)
                    .SingleAsync(cancellationToken)
                    .ConfigureAwait(false);
                scheduleAnchorUtc = DateTime.SpecifyKind(scheduleAnchorUtc, DateTimeKind.Utc);
                replacement = update.NextOccurrenceFactory!(scheduleAnchorUtc);
                if (replacement is null || replacement.CronJobId != current.Id)
                {
                    return null;
                }

                await dbContext
                    .Set<TCronJob>()
                    .Where(x => x.Id == current.Id)
                    .ExecuteUpdateAsync(
                        setter => setter.SetProperty(x => x.NextDueUtc, replacement.ExecutionTime),
                        cancellationToken
                    )
                    .ConfigureAwait(false);
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
                                // KTD1a: the SAME SkippedReason the seeding migration writes, and the opposite
                                // accounting answer. This path rebases the projection and creates the replacement
                                // occurrence itself just below (or leaves a paused definition idle until resume), so
                                // the new schedule already owns what comes next. Stamping ReplacementOwed here would
                                // double-run every expression edit — which is exactly why the rule reads this column
                                // and never the free-form string the two producers share.
                                .SetProperty(x => x.Disposition, CronOccurrenceDisposition.Superseded)
                                .SetProperty(x => x.OwnerId, _ => null)
                                .SetProperty(x => x.LockedUntil, _ => null),
                        cancellationToken
                    )
                    .ConfigureAwait(false);

                if (!current.IsPaused)
                {
                    replacement!.CronJobId = current.Id;
                    var contractDefinition = await dbContext
                        .Set<TCronJob>()
                        .AsNoTracking()
                        .SingleAsync(x => x.Id == current.Id, cancellationToken)
                        .ConfigureAwait(false);
                    replacement.SnapshotContract(contractDefinition);
                    replacement.CronJob = null!;
                    await dbContext
                        .Set<CronJobOccurrenceEntity<TCronJob>>()
                        .AddAsync(replacement, cancellationToken)
                        .ConfigureAwait(false);
                }
            }

            var result = update.Definition;
            result.CorrelationId = current.CorrelationId;
            result.CausationId = current.CausationId;
            result.IsPaused = current.IsPaused;
            result.ScheduleRevision = revisionChanged ? current.ScheduleRevision + 1 : current.ScheduleRevision;
            result.EvaluationFingerprint = revisionChanged
                ? update.Definition.EvaluationFingerprint ?? current.EvaluationFingerprint
                : current.EvaluationFingerprint;
            result.FingerprintFailureCount = revisionChanged ? 0 : current.FingerprintFailureCount;
            result.FingerprintRetryAfterUtc = revisionChanged ? null : current.FingerprintRetryAfterUtc;
            result.CreatedAt = current.CreatedAt;
            result.UpdatedAt = operationTimeUtc;
            results[inputIndex] = result;
        }

        await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        // Read the committed schedule position back onto each result, the same way the pause and resume paths already
        // re-read their definition. The watermark is stamped by the DATABASE clock inside the update statement, so the
        // caller's definition instance cannot know it — and JobsManager publishes whatever this returns, so without
        // this the edit path would broadcast an unset position while the store holds the rebased one.
        var committedPositions = await dbContext
            .Set<TCronJob>()
            .AsNoTracking()
            .Where(x => definitionIds.Contains(x.Id))
            .Select(x => new
            {
                x.Id,
                x.ReconciledThroughUtc,
                x.NextDueUtc,
            })
            .ToDictionaryAsync(x => x.Id, cancellationToken)
            .ConfigureAwait(false);

        foreach (var result in results)
        {
            if (committedPositions.TryGetValue(result.Id, out var position))
            {
                result.ReconciledThroughUtc = position.ReconciledThroughUtc;
                result.NextDueUtc = position.NextDueUtc;
            }
        }

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

    public async Task<CronSchedulePositionSeedResult> InsertCronJobsAsync(
        TCronJob[] jobs,
        CronSchedulePositionSeeder seeder,
        CancellationToken cancellationToken = default
    )
    {
        Argument.IsNotNull(jobs);
        Argument.IsNotNull(seeder);

        if (jobs.Length == 0)
        {
            return CronSchedulePositionSeedResult.Empty;
        }

        await using var dbContext = await DbContextFactory
            .CreateDbContextAsync(cancellationToken)
            .ConfigureAwait(false);

        // One transaction so the anchor and the rows it positions commit together: a crash between them would leave a
        // definition claiming to be reconciled through an instant no row records. The statement clock read inside it
        // is what makes this safe on PostgreSQL, where an EF-translated DateTime.UtcNow would freeze at the
        // transaction's start instead — the same rule the coordinated path is bound by, kept identical here so the two
        // creation paths cannot drift.
        await using var transaction = await dbContext
            .Database.BeginTransactionAsync(cancellationToken)
            .ConfigureAwait(false);

        var storeUtcNow = await JobsStoreClock
            .GetStatementUtcNowAsync(dbContext, cancellationToken)
            .ConfigureAwait(false);

        var earliestNextDueUtc = _ApplySchedulePositionSeed(jobs, seeder, storeUtcNow);

        await dbContext.Set<TCronJob>().AddRangeAsync(jobs, cancellationToken).ConfigureAwait(false);
        var affected = await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);

        await InvalidateCronExpressionsCacheAsync().ConfigureAwait(false);

        return new CronSchedulePositionSeedResult
        {
            StoreUtcNow = storeUtcNow,
            AffectedRows = affected,
            EarliestNextDueUtc = earliestNextDueUtc,
        };
    }

    public async Task<int> UpdateCronJobsAsync(TCronJob[] cronJobs, CancellationToken cancellationToken = default)
    {
        await using var dbContext = await DbContextFactory
            .CreateDbContextAsync(cancellationToken)
            .ConfigureAwait(false);

        dbContext.Set<TCronJob>().UpdateRange(cronJobs);

        // Dashboard forms omit lineage; preserve the original scheduling cause for future occurrences.
        foreach (var entry in dbContext.ChangeTracker.Entries<TCronJob>())
        {
            entry.Property(nameof(Entities.BaseEntity.BaseJobEntity.CorrelationId)).IsModified = false;
            entry.Property(nameof(Entities.BaseEntity.BaseJobEntity.CausationId)).IsModified = false;
        }

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
        await using var transaction = await dbContext
            .Database.BeginTransactionAsync(cancellationToken)
            .ConfigureAwait(false);
        var definitions = new Dictionary<Guid, TCronJob>();
        foreach (var definitionId in cronJobOccurrences.Select(x => x.CronJobId).Distinct().Order())
        {
            // Serialize every materialization with definition edits, including payload-only changes.
            var affected = await dbContext
                .Set<TCronJob>()
                .Where(x => x.Id == definitionId)
                .ExecuteUpdateAsync(
                    setters => setters.SetProperty(x => x.ScheduleRevision, x => x.ScheduleRevision),
                    cancellationToken
                )
                .ConfigureAwait(false);
            if (affected == 0)
            {
                throw new InvalidOperationException("A cron occurrence requires an existing definition.");
            }
            definitions.Add(
                definitionId,
                await dbContext
                    .Set<TCronJob>()
                    .AsNoTracking()
                    .SingleAsync(x => x.Id == definitionId, cancellationToken)
                    .ConfigureAwait(false)
            );
        }
        foreach (var occurrence in cronJobOccurrences)
        {
            occurrence.SnapshotContract(definitions[occurrence.CronJobId]);
            occurrence.CronJob = null!;
        }
        await dbContext
            .Set<CronJobOccurrenceEntity<TCronJob>>()
            .AddRangeAsync(cronJobOccurrences, cancellationToken)
            .ConfigureAwait(false);
        var inserted = await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        return inserted;
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
