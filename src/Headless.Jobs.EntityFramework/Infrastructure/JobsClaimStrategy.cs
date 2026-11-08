// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Runtime.CompilerServices;
using Headless.Jobs.Internal;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

#pragma warning disable MA0133 // EF must keep DateTime.UtcNow in expression trees so providers translate the database clock before the DateTimeOffset assignment.
namespace Headless.Jobs.Infrastructure;

internal static class JobsClaimStrategyDefaults
{
    public const int MaxCandidatePageSize = 1000;
    public const int MaxClaimBatchSize = 100;
}

/// <summary>Counts the rows a claim selected but lost to another node, for the claim-conflict metric.</summary>
internal static class JobsClaimConflicts
{
    /// <summary>
    /// The candidates a direct claim was asked for and did not return. A candidate the run filter excludes, or one of a
    /// cluster-limited function, is not counted: the claim may have refused it for a reason other than a lost race.
    /// </summary>
    /// <remarks>
    /// A claim that filled its <see cref="JobsClaimStrategyDefaults.MaxClaimBatchSize"/> budget never attempted the
    /// candidates past it, and a row lost to another node was replaced by the next one, so no loss can be told apart
    /// from a skip and none is counted.
    /// </remarks>
    public static int CountLost<TClaimed>(
        IEnumerable<TimeJobEntity> candidates,
        IReadOnlyDictionary<Guid, TClaimed> claimed,
        JobsRunFilter runFilter,
        JobsClusterConcurrency clusterConcurrency
    )
    {
        if (claimed.Count >= JobsClaimStrategyDefaults.MaxClaimBatchSize)
        {
            return 0;
        }

        return candidates
            .DistinctBy(x => x.Id)
            .Count(x =>
                !claimed.ContainsKey(x.Id) && runFilter.Allows(x.Function) && !clusterConcurrency.IsLimited(x.Function)
            );
    }
}

internal interface IJobsClaimStrategy<TTimeJob, TCronJob>
    where TTimeJob : TimeJobEntity<TTimeJob>, new()
    where TCronJob : CronJobEntity, new()
{
    IAsyncEnumerable<TimeJobEntity> ClaimTimeJobsAsync(TimeJobEntity[] timeJobs, CancellationToken cancellationToken);

    IAsyncEnumerable<TimeJobEntity> ClaimTimedOutTimeJobsAsync(CancellationToken cancellationToken);

    IAsyncEnumerable<CronJobOccurrenceEntity<TCronJob>> ClaimCronJobOccurrencesAsync(
        (DateTime Key, JobManagerDispatchContext[] Items) cronJobOccurrences,
        CancellationToken cancellationToken
    );

    IAsyncEnumerable<CronJobOccurrenceEntity<TCronJob>> ClaimTimedOutCronJobOccurrencesAsync(
        CancellationToken cancellationToken
    );
}

internal sealed partial class CompatibleJobsClaimStrategy<TDbContext, TTimeJob, TCronJob>(
    IDbContextFactory<TDbContext> dbContextFactory,
    IJobsClaimStrategy<TTimeJob, TCronJob> nativeStrategy,
    IJobsClaimStrategy<TTimeJob, TCronJob> casStrategy,
    ILogger<CompatibleJobsClaimStrategy<TDbContext, TTimeJob, TCronJob>>? logger = null
) : IJobsClaimStrategy<TTimeJob, TCronJob>
    where TDbContext : DbContext
    where TTimeJob : TimeJobEntity<TTimeJob>, new()
    where TCronJob : CronJobEntity, new()
{
    private readonly Lock _compatibilityLock = new();

    private readonly ILogger _logger =
        logger ?? NullLogger<CompatibleJobsClaimStrategy<TDbContext, TTimeJob, TCronJob>>.Instance;

    private IJobsClaimStrategy<TTimeJob, TCronJob>? _selectedStrategy;

    public async IAsyncEnumerable<TimeJobEntity> ClaimTimeJobsAsync(
        TimeJobEntity[] timeJobs,
        [EnumeratorCancellation] CancellationToken cancellationToken
    )
    {
        var strategy = _GetStrategy();
        await foreach (var job in strategy.ClaimTimeJobsAsync(timeJobs, cancellationToken).ConfigureAwait(false))
        {
            yield return job;
        }
    }

    public async IAsyncEnumerable<TimeJobEntity> ClaimTimedOutTimeJobsAsync(
        [EnumeratorCancellation] CancellationToken cancellationToken
    )
    {
        var strategy = _GetStrategy();
        await foreach (var job in strategy.ClaimTimedOutTimeJobsAsync(cancellationToken).ConfigureAwait(false))
        {
            yield return job;
        }
    }

    public async IAsyncEnumerable<CronJobOccurrenceEntity<TCronJob>> ClaimCronJobOccurrencesAsync(
        (DateTime Key, JobManagerDispatchContext[] Items) cronJobOccurrences,
        [EnumeratorCancellation] CancellationToken cancellationToken
    )
    {
        var strategy = _GetStrategy();
        await foreach (
            var occurrence in strategy
                .ClaimCronJobOccurrencesAsync(cronJobOccurrences, cancellationToken)
                .ConfigureAwait(false)
        )
        {
            yield return occurrence;
        }
    }

    public async IAsyncEnumerable<CronJobOccurrenceEntity<TCronJob>> ClaimTimedOutCronJobOccurrencesAsync(
        [EnumeratorCancellation] CancellationToken cancellationToken
    )
    {
        var strategy = _GetStrategy();
        await foreach (
            var occurrence in strategy.ClaimTimedOutCronJobOccurrencesAsync(cancellationToken).ConfigureAwait(false)
        )
        {
            yield return occurrence;
        }
    }

    /// <summary>
    /// Selects the strategy now and returns why the native claim cannot run against the model, or
    /// <see langword="null"/> when it can.
    /// </summary>
    internal string? FindCasFallbackReason()
    {
        if (!ReferenceEquals(_GetStrategy(), casStrategy))
        {
            return null;
        }

        using var dbContext = dbContextFactory.CreateDbContext();

        return NativeJobsClaimCompatibility.FindIncompatibility<TTimeJob, TCronJob>(dbContext.Model);
    }

    private IJobsClaimStrategy<TTimeJob, TCronJob> _GetStrategy()
    {
        lock (_compatibilityLock)
        {
            if (_selectedStrategy is not null)
            {
                return _selectedStrategy;
            }

            using var dbContext = dbContextFactory.CreateDbContext();
            var incompatibility = NativeJobsClaimCompatibility.FindIncompatibility<TTimeJob, TCronJob>(dbContext.Model);

            if (incompatibility is null)
            {
                return _selectedStrategy = nativeStrategy;
            }

            LogCasFallback(_logger, typeof(TDbContext).Name, incompatibility);
            return _selectedStrategy = casStrategy;
        }
    }

    [LoggerMessage(
        EventId = 20101,
        Level = LogLevel.Warning,
        Message = "Native Jobs claiming is incompatible with DbContext {DbContext}; using EF CAS claiming instead. Reason: {Reason}"
    )]
    private static partial void LogCasFallback(ILogger logger, string dbContext, string reason);
}

internal static class NativeJobsClaimCompatibility
{
    public static string? FindIncompatibility<TTimeJob, TCronJob>(IModel model)
        where TTimeJob : TimeJobEntity<TTimeJob>, new()
        where TCronJob : CronJobEntity, new()
    {
        Type[] jobTypes = [typeof(TTimeJob), typeof(TCronJob), typeof(CronJobOccurrenceEntity<TCronJob>)];
        var entityTypes = jobTypes.Select(model.FindEntityType).Where(x => x is not null).Cast<IEntityType>().ToArray();

        if (entityTypes.FirstOrDefault(x => x.GetDeclaredQueryFilters().Count > 0) is { } filtered)
        {
            return $"entity {filtered.DisplayName()} has a global query filter";
        }

        if (entityTypes.FirstOrDefault(x => x.GetDiscriminatorPropertyName() is not null) is { } discriminated)
        {
            return $"entity {discriminated.DisplayName()} uses discriminator-based inheritance";
        }

        foreach (var entityType in entityTypes)
        {
            var tableName = entityType.GetTableName();
            if (tableName is null)
            {
                continue;
            }

            var schema = entityType.GetSchema();
            var sharesTable = model
                .GetEntityTypes()
                .Any(other =>
                    other != entityType
                    && string.Equals(other.GetTableName(), tableName, StringComparison.Ordinal)
                    && string.Equals(other.GetSchema(), schema, StringComparison.Ordinal)
                );
            if (sharesTable)
            {
                return $"entity {entityType.DisplayName()} shares table {schema ?? "<default>"}.{tableName}";
            }
        }

        return null;
    }
}

internal sealed class EfCoreCasJobsClaimStrategy<TDbContext, TTimeJob, TCronJob>(
    IDbContextFactory<TDbContext> dbContextFactory,
    TimeProvider timeProvider,
    IGuidGenerator guidGenerator,
    IJobsOwnerIdentity ownerIdentity,
    SchedulerOptionsBuilder optionsBuilder,
    JobsRunFilter? runFilter = null,
    JobsClusterConcurrency? clusterConcurrency = null
) : IJobsClaimStrategy<TTimeJob, TCronJob>
    where TDbContext : DbContext
    where TTimeJob : TimeJobEntity<TTimeJob>, new()
    where TCronJob : CronJobEntity, new()
{
    private readonly TimeSpan _leaseDuration = optionsBuilder.LeaseDuration;

    // Gates every root claim, including the direct claim whose candidates a filtered peek already chose, so a claim
    // never depends on its caller having filtered.
    private readonly JobsRunFilter _runFilter = runFilter ?? JobsRunFilter.All;

    // The maximum number of nodes on a root-to-leaf path the tree claim leases (root = depth 1). A timed
    // descendant is a boundary — not descended into, claimed independently.
    private readonly int _maxChainDepth = optionsBuilder.MaxChainDepth;

    // The CAS claim leases in autocommit statements so PostgreSQL's transaction-start now() never shortens a lease, and a
    // cluster limit needs its lock and count in the same transaction as the lease. It refuses instead of overshooting.
    private readonly JobsClusterConcurrency _clusterConcurrency = clusterConcurrency ?? JobsClusterConcurrency.None;

    // Test seam: when set, invoked once between the root claim and the first descendant lease so a test can
    // deterministically invalidate the root lease and drive the frontier fence. Always null in production.
    internal Func<Task>? OnFrontierBeforeLease { get; set; }

    // Test seam: when set, the timed-out sweep invokes it with each peeked root after the peek and before that root's
    // claim, so a test can deterministically change the row between the two. Always null in production.
    internal Func<TimeJobEntity, Task>? OnFallbackBeforeClaim { get; set; }

    public async IAsyncEnumerable<TimeJobEntity> ClaimTimeJobsAsync(
        TimeJobEntity[] timeJobs,
        [EnumeratorCancellation] CancellationToken cancellationToken
    )
    {
        _ThrowIfClusterLimited();
        if (!ownerIdentity.TryGetStampOwner(out var owner))
        {
            yield break;
        }

        await using var dbContext = await dbContextFactory
            .CreateDbContextAsync(cancellationToken)
            .ConfigureAwait(false);

        // Claimed and yielded ONE root at a time on purpose: a consumer that stops enumerating (cancellation, host
        // stop) must leave the remaining candidates unclaimed for the next sweep rather than stranding rows it will
        // never execute under a lease. Pinned by
        // compatibility_fallback_claims_at_most_one_native_sized_batch_per_sweep.
        foreach (var timeJob in timeJobs)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var rootId = timeJob.Id;
            var expectedUpdatedAt = timeJob.UpdatedAt;
            // Gate = CAS token + full claimability, applied INLINE on the updated row (see _ClaimTimeJobTreeAsync
            // for why a subquery gate is unsound). The acquire predicate closes the same-clock-tick token residue:
            // even if a racing winner's DB-clock UpdatedAt lands on the peeked value, its fresh foreign lease
            // fails the loser's re-evaluated claimability arms.
            var claim = await _ClaimTimeJobTreeAsync(
                    dbContext,
                    q =>
                        q.Where(x => x.UpdatedAt == expectedUpdatedAt)
                            .Where(x =>
                                x.ExecutionTime == null
                                || x.ExecutionTime <= DateTime.UtcNow.AddSeconds(JobsDueSlack.Seconds)
                            )
                            .WhereRunnable(_runFilter)
                            .WhereCanAcquireUsingDatabaseClock(owner),
                    set =>
                        $"({set.Column(nameof(TimeJobEntity.ExecutionTime))} IS NULL OR {set.Column(nameof(TimeJobEntity.ExecutionTime))} <= {set.DatabaseNowSql(nameof(TimeJobEntity.ExecutionTime), TimeSpan.FromSeconds(JobsDueSlack.Seconds))})",
                    rootId,
                    owner,
                    cancellationToken
                )
                .ConfigureAwait(false);

            if (claim is not { } claimed)
            {
                // A function the run filter excludes is refused by the gate, not lost to another node.
                if (_runFilter.Allows(timeJob.Function))
                {
                    JobsMetrics.ClaimConflicts(JobsMetrics.ClaimStrategyCas, 1);
                }

                continue;
            }

            timeJob.UpdatedAt = claimed.UpdatedAt;
            timeJob.Status = JobStatus.Queued;

            // The peek-hydrated tree may include non-idle nodes (and their tails) the claim did not lease;
            // execute strictly the claimed set so nothing runs unclaimed.
            TimeJobSubtreeOperations.PruneToClaimedSet(timeJob, [.. claimed.Generations.Keys]);
            JobsLeaseGrant.StampClaimedTree(timeJob, claimed.Generations, claimed.ExpiresAt, owner);

            yield return timeJob;
        }
    }

    public async IAsyncEnumerable<TimeJobEntity> ClaimTimedOutTimeJobsAsync(
        [EnumeratorCancellation] CancellationToken cancellationToken
    )
    {
        _ThrowIfClusterLimited();
        if (!ownerIdentity.TryGetStampOwner(out var owner))
        {
            yield break;
        }

        await using var dbContext = await dbContextFactory
            .CreateDbContextAsync(cancellationToken)
            .ConfigureAwait(false);

        var context = dbContext.Set<TTimeJob>();
        var now = timeProvider.GetUtcNow();

        // Flat root load + in-memory rebuild of the non-timed subtree to MaxChainDepth (replaces a fixed-depth
        // nested projection).
        // The due-time arm uses the DATABASE clock (EF translates DateTime.UtcNow), matching the lease arm in the
        // same predicate — a node with a fast local clock must not claim jobs before they are due.
        var timeJobsToUpdate = await context
            .AsNoTracking()
            .Where(x => x.ExecutionTime != null)
            .WhereRunnable(_runFilter)
            .WhereCanFallbackClaimUsingDatabaseClock()
            .Where(x => x.ExecutionTime <= DateTime.UtcNow.AddSeconds(-1))
            // The fallback selects timed rows directly (ExecutionTime != null), so a timed descendant is
            // gated here too — claimable only once its parent reached its matching terminal state.
            .WhereClaimableUnderParentTerminalGate(context)
            .OrderBy(x => x.ExecutionTime)
            .ThenBy(x => x.Id)
            .Take(JobsClaimStrategyDefaults.MaxClaimBatchSize)
            .Select(MappingExtensions.ForFlatTimeJob<TTimeJob>())
            .ToArrayAsync(cancellationToken)
            .ConfigureAwait(false);

        await MappingExtensions
            .AttachNonTimedDescendantsAsync(context.AsNoTracking(), timeJobsToUpdate, _maxChainDepth, cancellationToken)
            .ConfigureAwait(false);

        // One root claimed and yielded per step (see ClaimTimeJobsAsync): abandoning the sweep must not leave rows
        // leased that the caller never receives.
        foreach (var timeJob in timeJobsToUpdate)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (OnFallbackBeforeClaim is not null)
            {
                await OnFallbackBeforeClaim(timeJob).ConfigureAwait(false);
            }

            var rootId = timeJob.Id;
            var expectedUpdatedAt = timeJob.UpdatedAt;
            // Re-assert the parent gate inside the atomic claim, INLINE on the updated row (see
            // _ClaimTimeJobTreeAsync). The parent-terminal gate remains a subquery by nature (it reads the PARENT
            // row) — that residue races only on a parent's one-way terminal transition, not on same-row ownership.
            var claim = await _ClaimTimeJobTreeAsync(
                    dbContext,
                    q =>
                        // An exact token, plus the peek's due-time test on the database clock: a root rescheduled after
                        // the peek, even by a writer whose clock stamped an equal or earlier UpdatedAt, must not run early.
                        q.Where(x => x.UpdatedAt == expectedUpdatedAt)
                            .Where(x => x.ExecutionTime != null && x.ExecutionTime <= DateTime.UtcNow.AddSeconds(-1))
                            .WhereRunnable(_runFilter)
                            .WhereCanFallbackClaimUsingDatabaseClock()
                            .WhereClaimableUnderParentTerminalGate(context),
                    set =>
                        $"{set.Column(nameof(TimeJobEntity.ExecutionTime))} <= {set.DatabaseNowSql(nameof(TimeJobEntity.ExecutionTime), TimeSpan.FromSeconds(-1))}",
                    rootId,
                    owner,
                    cancellationToken
                )
                .ConfigureAwait(false);

            if (claim is not { } claimed)
            {
                // The sweep selected this row as claimable; it is gone because another node claimed it first.
                JobsMetrics.ClaimConflicts(JobsMetrics.ClaimStrategyCas, 1);
                continue;
            }

            timeJob.UpdatedAt = claimed.UpdatedAt;
            timeJob.Status = JobStatus.Queued;

            // Prune the peek-hydrated tree to the claimed set so a node the claim stopped at never executes.
            TimeJobSubtreeOperations.PruneToClaimedSet(timeJob, [.. claimed.Generations.Keys]);
            JobsLeaseGrant.StampClaimedTree(timeJob, claimed.Generations, claimed.ExpiresAt, owner);

            yield return timeJob;
        }
    }

    public async IAsyncEnumerable<CronJobOccurrenceEntity<TCronJob>> ClaimTimedOutCronJobOccurrencesAsync(
        [EnumeratorCancellation] CancellationToken cancellationToken
    )
    {
        _ThrowIfClusterLimited();
        if (!ownerIdentity.TryGetStampOwner(out var owner))
        {
            yield break;
        }

        var now = timeProvider.GetUtcNow();

        await using var dbContext = await dbContextFactory
            .CreateDbContextAsync(cancellationToken)
            .ConfigureAwait(false);
        var context = dbContext.Set<CronJobOccurrenceEntity<TCronJob>>();
        // Due-time arm on the DATABASE clock, matching the lease arm (see ClaimTimedOutTimeJobsAsync).
        var cronJobsToUpdate = await context
            .AsNoTracking()
            .Where(x => !x.CronJob.IsPaused)
            .WhereRunnable(_runFilter)
            .WhereCanFallbackClaimUsingDatabaseClock()
            .Where(x => x.ExecutionTime <= DateTime.UtcNow.AddSeconds(-1))
            .OrderBy(x => x.ExecutionTime)
            .ThenBy(x => x.Id)
            .Take(JobsClaimStrategyDefaults.MaxClaimBatchSize)
            .Include(x => x.CronJob)
            .Select(MappingExtensions.ForQueueCronJobOccurrence<CronJobOccurrenceEntity<TCronJob>, TCronJob>())
            .ToArrayAsync(cancellationToken)
            .ConfigureAwait(false);

        foreach (var cronJobOccurrence in cronJobsToUpdate)
        {
            cancellationToken.ThrowIfCancellationRequested();

            // The peek's version token, checked on the row the grant updates, so an occurrence edited or claimed since
            // the peek is left alone; the grant's own fence refuses a row another attempt still holds.
            var set = JobsRowSet.For<CronJobOccurrenceEntity<TCronJob>>(dbContext);
            var condition =
                $"{set.Column(nameof(CronJobOccurrenceEntity<>.UpdatedAt))} = {set.Parameter(nameof(CronJobOccurrenceEntity<>.UpdatedAt), cronJobOccurrence.UpdatedAt)}"
                + $" AND {_StatusIn(set, JobStatus.Idle, JobStatus.Queued)}";
            set.Value(nameof(CronJobOccurrenceEntity<>.Status), JobStatus.Queued)
                .DatabaseNow(nameof(CronJobOccurrenceEntity<>.UpdatedAt));

            var granted = await JobsLeaseGrant
                .GrantAsync<CronJobOccurrenceEntity<TCronJob>>(
                    JobsLeaseRows.CronOccurrences<TCronJob>(dbContext),
                    dbContext,
                    set,
                    [cronJobOccurrence.Id],
                    condition,
                    deadline: null,
                    _leaseDuration,
                    owner,
                    reclaimOwn: false,
                    cancellationToken
                )
                .ConfigureAwait(false);

            if (granted.Count == 0)
            {
                JobsMetrics.ClaimConflicts(JobsMetrics.ClaimStrategyCas, 1);
                continue;
            }

            cronJobOccurrence.UpdatedAt = await context
                .AsNoTracking()
                .Where(x => x.Id == cronJobOccurrence.Id)
                .Select(x => x.UpdatedAt)
                .SingleAsync(cancellationToken)
                .ConfigureAwait(false);
            cronJobOccurrence.Status = JobStatus.Queued;
            _StampLease(cronJobOccurrence, granted[0], owner);

            yield return cronJobOccurrence;
        }
    }

    public async IAsyncEnumerable<CronJobOccurrenceEntity<TCronJob>> ClaimCronJobOccurrencesAsync(
        (DateTime Key, JobManagerDispatchContext[] Items) cronJobOccurrences,
        [EnumeratorCancellation] CancellationToken cancellationToken
    )
    {
        _ThrowIfClusterLimited();
        if (!ownerIdentity.TryGetStampOwner(out var owner))
        {
            yield break;
        }

        var now = timeProvider.GetUtcNow();
        var executionTime = cronJobOccurrences.Key;

        // The definition fences, the occurrence inserts, and the reuse pairing commit together, inside the context's
        // execution strategy so a retrying strategy replays the whole wave; the lease stamp below stays autocommit.
        // Lost rows are counted per attempt and reported once the wave commits, so a replayed attempt is not counted twice.
        var (claimResults, claimableOccurrenceIds, lostCount) = await JobsStoreTransaction
            .ExecuteAsync<
                TDbContext,
                (CronJobOccurrenceEntity<TCronJob>?[] Results, List<Guid> ClaimableIds, int Lost)
            >(
                dbContextFactory,
                async (attempt, _) =>
                {
                    var dbContext = attempt.DbContext;
                    var context = dbContext.Set<CronJobOccurrenceEntity<TCronJob>>();
                    var claimResults = new CronJobOccurrenceEntity<TCronJob>?[cronJobOccurrences.Items.Length];
                    var claimableOccurrenceIds = new List<Guid>();
                    var lost = 0;

                    // R3b: ONE occupancy probe per claim wave instead of one round trip per candidate. Every item in
                    // the wave shares the same execution instant (it is the batch key), so the accounting question is a
                    // single indexed read over (ExecutionTime, CronJobId). Reading it before the loop widens the window
                    // between probe and insert, which is harmless: only live rows can appear in that window, and the
                    // filtered unique index rejects the insert that would collide with one.
                    var insertCandidateIds = cronJobOccurrences
                        .Items.Where(x => x.NextCronOccurrence is null)
                        .Select(x => x.Id)
                        .Distinct()
                        .ToArray();
                    var accountedDefinitionIds =
                        insertCandidateIds.Length == 0
                            ? []
                            : (
                                await context
                                    .AsNoTracking()
                                    .Where(x =>
                                        insertCandidateIds.Contains(x.CronJobId) && x.ExecutionTime == executionTime
                                    )
                                    .Select(CronOccurrenceAccounting.InstantViewSelector<TCronJob>())
                                    .ToArrayAsync(cancellationToken)
                                    .ConfigureAwait(false)
                            )
                                .Where(x => x.AccountsForInstant)
                                .Select(x => x.CronJobId)
                                .ToHashSet();

                    for (var index = 0; index < cronJobOccurrences.Items.Length; index++)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        var item = cronJobOccurrences.Items[index];

                        var definitionAccepted = await dbContext
                            .Set<TCronJob>()
                            .Where(x => x.Id == item.Id && !x.IsPaused && x.ScheduleRevision == item.ScheduleRevision)
                            .WhereDefinitionRunnable(_runFilter)
                            .ExecuteUpdateAsync(
                                setter => setter.SetProperty(x => x.ScheduleRevision, x => x.ScheduleRevision),
                                cancellationToken
                            )
                            .ConfigureAwait(false);
                        if (definitionAccepted == 0)
                        {
                            continue;
                        }

                        if (item.NextCronOccurrence is null)
                        {
                            // A row that ACCOUNTS for this instant — including a terminal one the reuse pairing cannot
                            // see (its read is filtered to claimable rows) — means the advance stands. The filtered
                            // unique index blocks duplicates only among live rows, so without this check a completed
                            // resume-created occurrence would be re-materialized and the tick would run twice. The one
                            // row that does NOT account is the seeding migration's ReplacementOwed retirement, whose
                            // fire is still owed; it must fall through and insert. A live row left unclaimed here is
                            // picked up by the fallback sweep; nothing is disturbed either way.
                            if (accountedDefinitionIds.Contains(item.Id))
                            {
                                // The dispatch found no occurrence for this instant; another node has written one since.
                                lost++;
                                continue;
                            }

                            var itemToAdd = new CronJobOccurrenceEntity<TCronJob>
                            {
                                Id = guidGenerator.Create(),
                                Status = JobStatus.Idle,
                                ExecutionTime = executionTime,
                                CronJobId = item.Id,
                                OnNodeDeath = item.OnNodeDeath,
                                CreatedAt = now,
                                UpdatedAt = now,
                            };

                            var definition = await dbContext
                                .Set<TCronJob>()
                                .AsNoTracking()
                                .SingleAsync(x => x.Id == item.Id, cancellationToken)
                                .ConfigureAwait(false);
                            itemToAdd.SnapshotContract(definition);
                            await context.AddAsync(itemToAdd, cancellationToken).ConfigureAwait(false);
                            try
                            {
                                await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
                            }
                            catch (DbUpdateException)
                            {
                                // The unique index rejected the insert: another node materialized the instant first.
                                dbContext.Entry(itemToAdd).State = EntityState.Detached;
                                lost++;
                                continue;
                            }

                            dbContext.Entry(itemToAdd).State = EntityState.Detached;
                            itemToAdd.Status = JobStatus.Queued;
                            itemToAdd.CronJob = MappingExtensions.ProjectCronJob<TCronJob>(item, owner);
                            claimResults[index] = itemToAdd;
                            claimableOccurrenceIds.Add(itemToAdd.Id);
                            continue;
                        }

                        var affectedUpdate = await context
                            .Where(x => x.Id == item.NextCronOccurrence.Id)
                            .Where(x => x.ExecutionTime == executionTime)
                            .WhereCanAcquireUsingDatabaseClock(owner)
                            .ExecuteUpdateAsync(
                                prop =>
                                    prop.SetProperty(y => y.Status, y => y.Status)
                                        .SetProperty(y => y.OnNodeDeath, item.OnNodeDeath),
                                cancellationToken
                            )
                            .ConfigureAwait(false);

                        if (affectedUpdate <= 0)
                        {
                            lost++;
                            continue;
                        }

                        claimResults[index] = await context
                            .AsNoTracking()
                            .Include(x => x.CronJob)
                            .SingleAsync(x => x.Id == item.NextCronOccurrence.Id, cancellationToken)
                            .ConfigureAwait(false);
                        claimableOccurrenceIds.Add(item.NextCronOccurrence.Id);
                    }

                    await attempt.CommitAsync(cancellationToken).ConfigureAwait(false);

                    return (claimResults, claimableOccurrenceIds, lost);
                },
                cancellationToken
            )
            .ConfigureAwait(false);

        JobsMetrics.ClaimConflicts(JobsMetrics.ClaimStrategyCas, lostCount);

        await using var dbContext = await dbContextFactory
            .CreateDbContextAsync(cancellationToken)
            .ConfigureAwait(false);
        var context = dbContext.Set<CronJobOccurrenceEntity<TCronJob>>();

        // LEASE-DEADLINE WRITE — autocommit, outside the wave's transaction, so the expiry is the statement's database
        // clock and not a transaction start. Only the rows this grant takes are returned.
        Dictionary<Guid, JobsGrantedRow> grants = [];

        if (claimableOccurrenceIds.Count > 0)
        {
            var set = JobsRowSet.For<CronJobOccurrenceEntity<TCronJob>>(dbContext);
            var condition = _StatusIn(set, JobStatus.Idle, JobStatus.Queued);
            set.Value(nameof(CronJobOccurrenceEntity<>.Status), JobStatus.Queued)
                .DatabaseNow(nameof(CronJobOccurrenceEntity<>.UpdatedAt));

            grants = (
                await JobsLeaseGrant
                    .GrantAsync<CronJobOccurrenceEntity<TCronJob>>(
                        JobsLeaseRows.CronOccurrences<TCronJob>(dbContext),
                        dbContext,
                        set,
                        claimableOccurrenceIds,
                        condition,
                        deadline: null,
                        _leaseDuration,
                        owner,
                        // Mirrors the acquire predicate's owner arm; the new generation fences off the earlier dispatch.
                        reclaimOwn: true,
                        cancellationToken
                    )
                    .ConfigureAwait(false)
            ).ToDictionary(x => x.Id);
        }

        var grantedIds = grants.Keys.ToArray();
        var updatedAts = await context
            .AsNoTracking()
            .Where(x => grantedIds.Contains(x.Id))
            .Select(x => new { x.Id, x.UpdatedAt })
            .ToDictionaryAsync(x => x.Id, x => x.UpdatedAt, cancellationToken)
            .ConfigureAwait(false);

        foreach (var result in claimResults)
        {
            if (result is null || !grants.TryGetValue(result.Id, out var grant))
            {
                continue;
            }

            result.Status = JobStatus.Queued;
            result.UpdatedAt = updatedAts[result.Id];
            _StampLease(result, grant, owner);
            yield return result;
        }
    }

    private void _ThrowIfClusterLimited()
    {
        if (_clusterConcurrency.HasLimits)
        {
            throw new NotSupportedException(
                "Cluster-wide job concurrency needs a native claim provider: call UsePostgreSqlClaims() or "
                    + "UseSqlServerClaims() in UseEntityFramework, or keep the model compatible with the native claim. "
                    + $"Jobs with a ClusterMaxConcurrency: {string.Join(", ", _clusterConcurrency.LimitedFunctions)}."
            );
        }
    }

    private async Task<JobsClaimedTree?> _ClaimTimeJobTreeAsync(
        TDbContext dbContext,
        Func<IQueryable<TTimeJob>, IQueryable<TTimeJob>> rootGate,
        Func<JobsRowSet, string> inlineDue,
        Guid rootId,
        string owner,
        CancellationToken cancellationToken
    )
    {
        // Claim the root and its non-timed descendants down to MaxChainDepth, frontier by frontier.
        //
        // The EF gate reads the root's version token; the grant then re-checks that token, the due time, and "no live
        // attempt holds the row" INLINE on the row it updates, never through a subquery: under READ COMMITTED
        // PostgreSQL evaluates an uncorrelated subquery once against the statement snapshot and does NOT re-evaluate
        // it during EvalPlanQual after blocking on a concurrent claimant's row lock, so two racers both matched and
        // BOTH claimed the same root (reproduced by concurrent_cas_claims_of_one_row_have_exactly_one_winner). Inline
        // quals are re-checked against the winner's committed row version, so the loser matches nothing.
        //
        // The root grant runs in AUTOCOMMIT: inside a transaction PostgreSQL's now() freezes at transaction start and
        // silently shortens the lease. Each descendant then copies the root's stored deadline, so the whole tree
        // expires together.
        //
        // A crash mid-claim leaves a partially leased tree that heals itself: the caller prunes to the nodes this
        // attempt claimed, and re-claiming the root after its lease lapses grants every idle descendant afresh.
        var jobs = dbContext.Set<TTimeJob>();
        var candidate = await rootGate(jobs.AsNoTracking().Where(x => x.Id == rootId))
            .Select(x => new { x.UpdatedAt })
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);

        if (candidate is null)
        {
            return null;
        }

        var set = JobsRowSet.For<TTimeJob>(dbContext);
        var condition =
            $"{set.Column(nameof(TimeJobEntity.UpdatedAt))} = {set.Parameter(nameof(TimeJobEntity.UpdatedAt), candidate.UpdatedAt)}"
            + $" AND {_StatusIn(set, JobStatus.Idle, JobStatus.Queued)}"
            + $" AND {inlineDue(set)}";
        set.Value(nameof(TimeJobEntity.Status), JobStatus.Queued).DatabaseNow(nameof(TimeJobEntity.UpdatedAt));

        var granted = await JobsLeaseGrant
            .GrantAsync<TTimeJob>(
                JobsLeaseRows.TimeJobs<TTimeJob>(dbContext),
                dbContext,
                set,
                [rootId],
                condition,
                deadline: null,
                _leaseDuration,
                owner,
                // Mirrors the acquire predicate's owner arm; the new generation fences off the earlier dispatch.
                reclaimOwn: true,
                cancellationToken
            )
            .ConfigureAwait(false);

        if (granted.Count == 0)
        {
            return null;
        }

        // The frontier walk is shared with the immediate-dispatch acquire, so the deadline-copy discipline has one
        // relational implementation.
        var claimedByRoot = await JobsSubtreeLeaseWalk
            .LeaseNonTimedDescendantsAsync<TTimeJob>(
                dbContext,
                granted,
                owner,
                _leaseDuration,
                _maxChainDepth,
                OnFrontierBeforeLease,
                cancellationToken
            )
            .ConfigureAwait(false);

        var updatedAt = await jobs.AsNoTracking()
            .Where(x => x.Id == rootId)
            .Select(x => x.UpdatedAt)
            .SingleAsync(cancellationToken)
            .ConfigureAwait(false);

        return new JobsClaimedTree(claimedByRoot[rootId], granted[0].ExpiresAt, updatedAt);
    }

    private static string _StatusIn(JobsRowSet set, JobStatus first, JobStatus second)
    {
        var status = set.Column(nameof(TimeJobEntity.Status));

        return $"{status} IN ({set.Parameter(nameof(TimeJobEntity.Status), first)}, {set.Parameter(nameof(TimeJobEntity.Status), second)})";
    }

    private static void _StampLease(CronJobOccurrenceEntity<TCronJob> occurrence, JobsGrantedRow grant, string owner)
    {
        occurrence.Lease = occurrence.Lease with
        {
            Generation = grant.Generation,
            State = Headless.Fencing.RowLeaseState.Active,
            ExpiresAt = grant.ExpiresAt,
            Holder = owner,
            TakeoverCount = grant.TakeoverCount,
        };
    }

    private sealed record JobsClaimedTree(
        Dictionary<Guid, long> Generations,
        DateTimeOffset ExpiresAt,
        DateTimeOffset UpdatedAt
    );
}
