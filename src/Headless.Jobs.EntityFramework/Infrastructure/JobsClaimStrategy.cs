// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Runtime.CompilerServices;
using Headless.Abstractions;
using Headless.Jobs.Entities;
using Headless.Jobs.Enums;
using Headless.Jobs.Interfaces;
using Headless.Jobs.Internal;
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

    // KTD4 test seam: when set, invoked once between the root claim and the first descendant lease so a test can
    // deterministically invalidate the root lease and drive the frontier fence. Always null in production.
    internal Func<Task>? OnFrontierBeforeLease { get; set; }

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

        // Claimed and yielded ONE root at a time on purpose: a consumer that stops enumerating (cancellation, host
        // stop) must leave the remaining candidates unclaimed for the next sweep rather than stranding rows it will
        // never execute under a lease. Pinned by
        // compatibility_fallback_claims_at_most_one_native_sized_batch_per_sweep.
        foreach (var timeJob in timeJobs)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var rootId = timeJob.Id;
            var expectedUpdatedAt = timeJob.UpdatedAt;
            var rootMatches = context.Where(x => x.Id == rootId && x.UpdatedAt == expectedUpdatedAt);
            var claimedIds = await _ClaimTimeJobTreeAsync(context, rootMatches, rootId, owner, cancellationToken)
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
            TimeJobSubtreeOperations.PruneToClaimedSet(timeJob, claimedIds);

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
        var now = timeProvider.GetUtcNow();
        var fallbackThreshold = now.UtcDateTime.AddSeconds(-1);

        // R12/KTD2: flat root load + in-memory rebuild of the non-timed subtree to MaxChainDepth (replaces a fixed-depth
        // nested projection).
        var timeJobsToUpdate = await context
            .AsNoTracking()
            .Where(x => x.ExecutionTime != null)
            .WhereCanFallbackClaimUsingDatabaseClock()
            .Where(x => x.ExecutionTime <= fallbackThreshold)
            // U5/KTD3: the fallback selects timed rows directly (ExecutionTime != null), so a timed descendant is
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

            var rootId = timeJob.Id;
            var expectedUpdatedAt = timeJob.UpdatedAt;
            // U5/KTD3: re-assert the parent gate inside the atomic claim (rootMatches gates the root ExecuteUpdate), so
            // a timed descendant is never leased if its parent had not reached its matching terminal state.
            var rootMatches = context
                .Where(x => x.Id == rootId && x.UpdatedAt <= expectedUpdatedAt)
                .WhereCanFallbackClaimUsingDatabaseClock()
                .WhereClaimableUnderParentTerminalGate(context);
            var claimedIds = await _ClaimTimeJobTreeAsync(context, rootMatches, rootId, owner, cancellationToken)
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
            TimeJobSubtreeOperations.PruneToClaimedSet(timeJob, claimedIds);

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

        var now = timeProvider.GetUtcNow();
        var fallbackThreshold = now.UtcDateTime.AddSeconds(-1);

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

        var now = timeProvider.GetUtcNow();
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
        DbSet<TTimeJob> context,
        IQueryable<TTimeJob> rootMatches,
        Guid rootId,
        string owner,
        CancellationToken cancellationToken
    )
    {
        // R12/KTD2: claim the root and its non-timed descendants down to MaxChainDepth, frontier by frontier. Two
        // DB-clock lease invariants govern this (docs/solutions/design-patterns/atomic-database-clock-relational-lease-claims.md):
        //
        //   (1) The root lease-DEADLINE write runs in AUTOCOMMIT with the DB-clock expression — NEVER inside an
        //       explicit transaction, which would freeze PostgreSQL's now() at transaction-open and silently shorten
        //       the lease. This single UPDATE is already atomic and, gated on the optimistic rootMatches predicate, a
        //       losing racer sees 0 rows and never touches the descendants — the transaction added no atomicity the
        //       optimistic gate did not already provide.
        //   (2) Every descendant COPIES the root's persisted LockedUntil via a database-evaluated subquery (no clock
        //       function at all in descendant stamps), so all levels share the root's EXACT deadline on both
        //       PostgreSQL and SqlServer — a stronger single-claim-instant than a transaction gave (per-statement
        //       GETUTCDATE() would otherwise diverge descendant leases from the root's by ~10-20ms on SqlServer).
        //
        // Crash-mid-claim recovery (this replaces the dropped transaction's atomicity role): a partially stamped tree
        // is self-healing. PruneToClaimedSet yields only the nodes actually claimed in THIS attempt, so a half-stamped
        // tail never executes; the claimed-but-unexecuted root is reclaimed once its lease lapses (stalled-lease sweep
        // / claim predicate), and re-claiming re-stamps every idle descendant fresh.
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
            return [];
        }

        // The frontier lease-walk is shared with the immediate-dispatch acquire (JobsSubtreeLeaseWalk) so the KTD2
        // lease-deadline-copy discipline has exactly one relational implementation. This path walks a single root at a
        // time because it claims and yields incrementally; the walk batches across roots for callers that acquire a
        // whole set at once.
        var claimedIdsByRoot = await JobsSubtreeLeaseWalk
            .LeaseNonTimedDescendantsAsync(
                context,
                [rootId],
                owner,
                _maxChainDepth,
                OnFrontierBeforeLease,
                cancellationToken
            )
            .ConfigureAwait(false);

        return claimedIdsByRoot[rootId];
    }
}
