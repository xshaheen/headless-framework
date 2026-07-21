// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Runtime.CompilerServices;
using Headless.Abstractions;
using Headless.Jobs.Entities;
using Headless.Jobs.Enums;
using Headless.Jobs.Interfaces;
using Headless.Jobs.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Headless.Jobs.Infrastructure;

internal static class JobsClaimStrategyDefaults
{
    public const int MaxCandidatePageSize = 1000;
    public const int MaxClaimBatchSize = 100;
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
    SchedulerOptionsBuilder optionsBuilder
) : IJobsClaimStrategy<TTimeJob, TCronJob>
    where TDbContext : DbContext
    where TTimeJob : TimeJobEntity<TTimeJob>, new()
    where TCronJob : CronJobEntity, new()
{
    private readonly TimeSpan _leaseDuration = optionsBuilder.LeaseDuration;

    // R12/KTD2: the maximum number of nodes on a root-to-leaf path the tree claim leases (root = depth 1). A timed
    // descendant is a boundary — not descended into, claimed independently (U5).
    private readonly int _maxChainDepth = optionsBuilder.MaxChainDepth;

    public async IAsyncEnumerable<TimeJobEntity> ClaimTimeJobsAsync(
        TimeJobEntity[] timeJobs,
        [EnumeratorCancellation] CancellationToken cancellationToken
    )
    {
        if (!ownerIdentity.TryGetStampOwner(out var owner))
        {
            yield break;
        }

        await using var dbContext = await dbContextFactory
            .CreateDbContextAsync(cancellationToken)
            .ConfigureAwait(false);

        var context = dbContext.Set<TTimeJob>();

        foreach (var timeJob in timeJobs)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var rootId = timeJob.Id;
            var expectedUpdatedAt = timeJob.UpdatedAt;
            var rootMatches = context.Where(x => x.Id == rootId && x.UpdatedAt == expectedUpdatedAt);
            var claimedIds = await _ClaimTimeJobTreeAsync(dbContext, rootMatches, rootId, owner, cancellationToken)
                .ConfigureAwait(false);

            if (claimedIds.Count == 0)
            {
                continue;
            }

            var claimTimestamps = await context
                .AsNoTracking()
                .Where(x => x.Id == rootId)
                .Select(x => new { x.LockedUntil, x.UpdatedAt })
                .SingleAsync(cancellationToken)
                .ConfigureAwait(false);

            timeJob.UpdatedAt = claimTimestamps.UpdatedAt;
            timeJob.OwnerId = owner;
            timeJob.LockedUntil = claimTimestamps.LockedUntil;
            timeJob.Status = JobStatus.Queued;

            // KTD2: the peek-hydrated tree may include non-idle nodes (and their tails) the claim did not lease;
            // execute strictly the claimed set so nothing runs unclaimed.
            MappingExtensions.PruneToClaimedSet(timeJob, claimedIds);

            yield return timeJob;
        }
    }

    public async IAsyncEnumerable<TimeJobEntity> ClaimTimedOutTimeJobsAsync(
        [EnumeratorCancellation] CancellationToken cancellationToken
    )
    {
        if (!ownerIdentity.TryGetStampOwner(out var owner))
        {
            yield break;
        }

        await using var dbContext = await dbContextFactory
            .CreateDbContextAsync(cancellationToken)
            .ConfigureAwait(false);

        var context = dbContext.Set<TTimeJob>();
        var now = timeProvider.GetUtcNow().UtcDateTime;
        var fallbackThreshold = now.AddSeconds(-1);

        // R12/KTD2: flat root load + in-memory rebuild of the non-timed subtree to MaxChainDepth (replaces the fixed
        // two-level ForQueueTimeJobs projection).
        var timeJobsToUpdate = await context
            .AsNoTracking()
            .Where(x => x.ExecutionTime != null)
            .WhereCanFallbackClaimUsingDatabaseClock()
            .Where(x => x.ExecutionTime <= fallbackThreshold)
            .OrderBy(x => x.ExecutionTime)
            .ThenBy(x => x.Id)
            .Take(JobsClaimStrategyDefaults.MaxClaimBatchSize)
            .Select(MappingExtensions.ForFlatTimeJob<TTimeJob>())
            .ToArrayAsync(cancellationToken)
            .ConfigureAwait(false);

        await MappingExtensions
            .AttachNonTimedDescendantsAsync(context.AsNoTracking(), timeJobsToUpdate, _maxChainDepth, cancellationToken)
            .ConfigureAwait(false);

        foreach (var timeJob in timeJobsToUpdate)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var rootId = timeJob.Id;
            var expectedUpdatedAt = timeJob.UpdatedAt;
            var rootMatches = context
                .Where(x => x.Id == rootId && x.UpdatedAt <= expectedUpdatedAt)
                .WhereCanFallbackClaimUsingDatabaseClock();
            var claimedIds = await _ClaimTimeJobTreeAsync(dbContext, rootMatches, rootId, owner, cancellationToken)
                .ConfigureAwait(false);

            if (claimedIds.Count == 0)
            {
                continue;
            }

            var claimTimestamps = await context
                .AsNoTracking()
                .Where(x => x.Id == rootId)
                .Select(x => new { x.LockedUntil, x.UpdatedAt })
                .SingleAsync(cancellationToken)
                .ConfigureAwait(false);

            timeJob.OwnerId = owner;
            timeJob.LockedUntil = claimTimestamps.LockedUntil;
            timeJob.UpdatedAt = claimTimestamps.UpdatedAt;
            timeJob.Status = JobStatus.Queued;

            // KTD2: prune the peek-hydrated tree to the claimed set so a node the claim stopped at never executes.
            MappingExtensions.PruneToClaimedSet(timeJob, claimedIds);

            yield return timeJob;
        }
    }

    public async IAsyncEnumerable<CronJobOccurrenceEntity<TCronJob>> ClaimTimedOutCronJobOccurrencesAsync(
        [EnumeratorCancellation] CancellationToken cancellationToken
    )
    {
        if (!ownerIdentity.TryGetStampOwner(out var owner))
        {
            yield break;
        }

        var now = timeProvider.GetUtcNow().UtcDateTime;
        var fallbackThreshold = now.AddSeconds(-1);

        await using var dbContext = await dbContextFactory
            .CreateDbContextAsync(cancellationToken)
            .ConfigureAwait(false);
        var context = dbContext.Set<CronJobOccurrenceEntity<TCronJob>>();
        var cronJobsToUpdate = await context
            .AsNoTracking()
            .Where(x => !x.CronJob.IsPaused)
            .WhereCanFallbackClaimUsingDatabaseClock()
            .Where(x => x.ExecutionTime <= fallbackThreshold)
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

            var affected = await context
                .Where(x => x.Id == cronJobOccurrence.Id && x.UpdatedAt == cronJobOccurrence.UpdatedAt)
                .WhereCanFallbackClaimUsingDatabaseClock()
                .ExecuteUpdateAsync(
                    setter =>
                        setter
                            .SetProperty(x => x.OwnerId, owner)
                            .SetProperty(
                                x => x.LockedUntil,
                                _ => DateTime.UtcNow.AddSeconds(_leaseDuration.TotalSeconds)
                            )
                            .SetProperty(x => x.UpdatedAt, _ => DateTime.UtcNow)
                            .SetProperty(x => x.Status, JobStatus.Queued),
                    cancellationToken
                )
                .ConfigureAwait(false);

            if (affected <= 0)
            {
                continue;
            }

            var claimTimestamps = await context
                .AsNoTracking()
                .Where(x => x.Id == cronJobOccurrence.Id)
                .Select(x => new { x.LockedUntil, x.UpdatedAt })
                .SingleAsync(cancellationToken)
                .ConfigureAwait(false);

            cronJobOccurrence.OwnerId = owner;
            cronJobOccurrence.LockedUntil = claimTimestamps.LockedUntil;
            cronJobOccurrence.UpdatedAt = claimTimestamps.UpdatedAt;
            cronJobOccurrence.Status = JobStatus.Queued;

            yield return cronJobOccurrence;
        }
    }

    public async IAsyncEnumerable<CronJobOccurrenceEntity<TCronJob>> ClaimCronJobOccurrencesAsync(
        (DateTime Key, JobManagerDispatchContext[] Items) cronJobOccurrences,
        [EnumeratorCancellation] CancellationToken cancellationToken
    )
    {
        if (!ownerIdentity.TryGetStampOwner(out var owner))
        {
            yield break;
        }

        var now = timeProvider.GetUtcNow().UtcDateTime;
        var executionTime = cronJobOccurrences.Key;

        await using var dbContext = await dbContextFactory
            .CreateDbContextAsync(cancellationToken)
            .ConfigureAwait(false);
        await using var transaction = await dbContext
            .Database.BeginTransactionAsync(cancellationToken)
            .ConfigureAwait(false);
        var context = dbContext.Set<CronJobOccurrenceEntity<TCronJob>>();
        var claimResults = new CronJobOccurrenceEntity<TCronJob>?[cronJobOccurrences.Items.Length];
        var claimableOccurrenceIds = new List<Guid>();

        for (var index = 0; index < cronJobOccurrences.Items.Length; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var item = cronJobOccurrences.Items[index];

            var definitionAccepted = await dbContext
                .Set<TCronJob>()
                .Where(x => x.Id == item.Id && !x.IsPaused && x.ScheduleRevision == item.ScheduleRevision)
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
                var itemToAdd = new CronJobOccurrenceEntity<TCronJob>
                {
                    Id = guidGenerator.Create(),
                    Status = JobStatus.Idle,
                    OwnerId = null,
                    ExecutionTime = executionTime,
                    CronJobId = item.Id,
                    LockedUntil = null,
                    OnNodeDeath = item.OnNodeDeath,
                    CreatedAt = now,
                    UpdatedAt = now,
                };

                await context.AddAsync(itemToAdd, cancellationToken).ConfigureAwait(false);
                try
                {
                    await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
                }
                catch (DbUpdateException)
                {
                    dbContext.Entry(itemToAdd).State = EntityState.Detached;
                    continue;
                }

                dbContext.Entry(itemToAdd).State = EntityState.Detached;
                itemToAdd.Status = JobStatus.Queued;
                itemToAdd.OwnerId = owner;
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
                continue;
            }

            claimResults[index] = new CronJobOccurrenceEntity<TCronJob>
            {
                Id = item.NextCronOccurrence.Id,
                CronJobId = item.Id,
                ExecutionTime = executionTime,
                Status = JobStatus.Queued,
                OwnerId = owner,
                OnNodeDeath = item.OnNodeDeath,
                CreatedAt = item.NextCronOccurrence.CreatedAt,
                CronJob = MappingExtensions.ProjectCronJob<TCronJob>(item, owner),
            };
            claimableOccurrenceIds.Add(item.NextCronOccurrence.Id);
        }

        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);

        if (claimableOccurrenceIds.Count > 0)
        {
            await context
                .Where(x => claimableOccurrenceIds.Contains(x.Id))
                .WhereCanAcquireUsingDatabaseClock(owner)
                .ExecuteUpdateAsync(
                    setter =>
                        setter
                            .SetProperty(x => x.OwnerId, owner)
                            .SetProperty(
                                x => x.LockedUntil,
                                _ => DateTime.UtcNow.AddSeconds(_leaseDuration.TotalSeconds)
                            )
                            .SetProperty(x => x.UpdatedAt, _ => DateTime.UtcNow)
                            .SetProperty(x => x.Status, JobStatus.Queued),
                    cancellationToken
                )
                .ConfigureAwait(false);
        }

        var claimedIds = claimResults.Where(x => x is not null).Select(x => x!.Id).ToArray();
        var claimTimestamps = await context
            .AsNoTracking()
            .Where(x => claimedIds.Contains(x.Id) && x.OwnerId == owner && x.Status == JobStatus.Queued)
            .Select(x => new
            {
                x.Id,
                x.LockedUntil,
                x.UpdatedAt,
            })
            .ToDictionaryAsync(x => x.Id, cancellationToken)
            .ConfigureAwait(false);

        foreach (var result in claimResults)
        {
            if (result is null || !claimTimestamps.TryGetValue(result.Id, out var timestamps))
            {
                continue;
            }

            result.LockedUntil = timestamps.LockedUntil;
            result.UpdatedAt = timestamps.UpdatedAt;
            yield return result;
        }
    }

    private async Task<HashSet<Guid>> _ClaimTimeJobTreeAsync(
        TDbContext dbContext,
        IQueryable<TTimeJob> rootMatches,
        Guid rootId,
        string owner,
        CancellationToken cancellationToken
    )
    {
        var context = dbContext.Set<TTimeJob>();

        // R12/KTD2: claim the root and its non-timed descendants down to MaxChainDepth, frontier by frontier, INSIDE
        // ONE TRANSACTION so a crash mid-claim cannot strand a partially claimed tree and every level shares one claim
        // instant. This multi-statement claim deliberately departs from the single-statement autocommit lease-stamp
        // rule: the transaction anchors the lease deadline (PostgreSQL `now()` == transaction-open) and shortens the
        // lease by the transaction's bounded (depth-many round-trip) duration — immaterial against a minute-scale
        // lease, the same trade-off the reclaim sweeps already accept. Claiming the root first, gated on the
        // optimistic rootMatches predicate, means a losing racer sees 0 rows and never touches the descendants.
        await using var transaction = await dbContext
            .Database.BeginTransactionAsync(cancellationToken)
            .ConfigureAwait(false);

        var rootAffected = await context
            .Where(x => x.Id == rootId)
            .Where(_ => rootMatches.Any())
            .ExecuteUpdateAsync(
                setter =>
                    setter
                        .SetProperty(x => x.OwnerId, owner)
                        .SetProperty(x => x.LockedUntil, _ => DateTime.UtcNow.AddSeconds(_leaseDuration.TotalSeconds))
                        .SetProperty(x => x.UpdatedAt, _ => DateTime.UtcNow)
                        .SetProperty(x => x.Status, JobStatus.Queued),
                cancellationToken
            )
            .ConfigureAwait(false);

        if (rootAffected <= 0)
        {
            await transaction.RollbackAsync(cancellationToken).ConfigureAwait(false);

            return [];
        }

        // KTD2: accumulate the exact set of claimed ids (root + leased descendants) so the caller rebuilds the tree
        // strictly from it — a node below a non-idle frontier is never leased and must never execute unclaimed.
        var claimedIds = new HashSet<Guid> { rootId };
        var frontier = new[] { rootId };
        var depth = 1;

        while (frontier.Length != 0 && depth < _maxChainDepth)
        {
            var parentIds = frontier;

            // Lease the idle non-timed children of the current frontier (status stays Idle — only the root is Queued).
            await context
                .Where(x =>
                    x.ParentId != null
                    && ((IEnumerable<Guid>)parentIds).Contains(x.ParentId.Value)
                    && x.Status == JobStatus.Idle
                    && x.ExecutionTime == null
                )
                .ExecuteUpdateAsync(
                    setter =>
                        setter
                            .SetProperty(x => x.OwnerId, owner)
                            .SetProperty(
                                x => x.LockedUntil,
                                _ => DateTime.UtcNow.AddSeconds(_leaseDuration.TotalSeconds)
                            )
                            .SetProperty(x => x.UpdatedAt, _ => DateTime.UtcNow),
                    cancellationToken
                )
                .ConfigureAwait(false);

            // Descend into exactly the non-timed children we just leased (idle, non-timed, now owned by us). A non-idle
            // child (terminalized/running) is not owned by us here, so the frontier stops there.
            frontier = await context
                .AsNoTracking()
                .Where(x =>
                    x.ParentId != null
                    && ((IEnumerable<Guid>)parentIds).Contains(x.ParentId.Value)
                    && x.ExecutionTime == null
                    && x.Status == JobStatus.Idle
                    && x.OwnerId == owner
                )
                .Select(x => x.Id)
                .ToArrayAsync(cancellationToken)
                .ConfigureAwait(false);

            foreach (var id in frontier)
            {
                claimedIds.Add(id);
            }

            depth++;
        }

        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);

        return claimedIds;
    }
}
