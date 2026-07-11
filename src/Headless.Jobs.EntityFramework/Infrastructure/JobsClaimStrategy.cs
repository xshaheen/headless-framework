// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Runtime.CompilerServices;
using Headless.Jobs.Entities;
using Headless.Jobs.Enums;
using Headless.Jobs.Interfaces;
using Headless.Jobs.Models;
using Microsoft.EntityFrameworkCore;

namespace Headless.Jobs.Infrastructure;

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

internal sealed class EfCoreCasJobsClaimStrategy<TDbContext, TTimeJob, TCronJob>(
    IDbContextFactory<TDbContext> dbContextFactory,
    TimeProvider timeProvider,
    IJobsOwnerIdentity ownerIdentity,
    SchedulerOptionsBuilder optionsBuilder
) : IJobsClaimStrategy<TTimeJob, TCronJob>
    where TDbContext : DbContext
    where TTimeJob : TimeJobEntity<TTimeJob>, new()
    where TCronJob : CronJobEntity, new()
{
    private readonly TimeSpan _leaseDuration = optionsBuilder.LeaseDuration;

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
        var now = timeProvider.GetUtcNow().UtcDateTime;

        foreach (var timeJob in timeJobs)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var rootId = timeJob.Id;
            var expectedUpdatedAt = timeJob.UpdatedAt;
            var rootMatches = context.Where(x => x.Id == rootId && x.UpdatedAt == expectedUpdatedAt);
            var affected = await _ClaimTimeJobTreeAsync(context, rootMatches, rootId, owner, now, cancellationToken)
                .ConfigureAwait(false);

            if (affected <= 0)
            {
                continue;
            }

            timeJob.UpdatedAt = now;
            timeJob.OwnerId = owner;
            timeJob.LockedUntil = now.Add(_leaseDuration);
            timeJob.Status = JobStatus.Queued;

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

        var timeJobsToUpdate = await context
            .AsNoTracking()
            .Where(x => x.ExecutionTime != null)
            .WhereCanFallbackClaim(now)
            .Where(x => x.ExecutionTime <= fallbackThreshold)
            .Include(x => x.Children.Where(y => y.ExecutionTime == null))
            .Select(MappingExtensions.ForQueueTimeJobs<TTimeJob>())
            .ToArrayAsync(cancellationToken)
            .ConfigureAwait(false);

        foreach (var timeJob in timeJobsToUpdate)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var rootId = timeJob.Id;
            var expectedUpdatedAt = timeJob.UpdatedAt;
            var rootMatches = context
                .Where(x => x.Id == rootId && x.UpdatedAt <= expectedUpdatedAt)
                .WhereCanFallbackClaim(now);
            var affected = await _ClaimTimeJobTreeAsync(context, rootMatches, rootId, owner, now, cancellationToken)
                .ConfigureAwait(false);

            if (affected <= 0)
            {
                continue;
            }

            timeJob.OwnerId = owner;
            timeJob.LockedUntil = now.Add(_leaseDuration);
            timeJob.UpdatedAt = now;
            timeJob.Status = JobStatus.Queued;

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
            .Include(x => x.CronJob)
            .WhereCanFallbackClaim(now)
            .Where(x => x.ExecutionTime <= fallbackThreshold)
            .Select(MappingExtensions.ForQueueCronJobOccurrence<CronJobOccurrenceEntity<TCronJob>, TCronJob>())
            .ToArrayAsync(cancellationToken)
            .ConfigureAwait(false);

        foreach (var cronJobOccurrence in cronJobsToUpdate)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var affected = await context
                .Where(x => x.Id == cronJobOccurrence.Id && x.UpdatedAt == cronJobOccurrence.UpdatedAt)
                .WhereCanFallbackClaim(now)
                .ExecuteUpdateAsync(
                    setter =>
                        setter
                            .SetProperty(x => x.OwnerId, owner)
                            .SetProperty(x => x.LockedUntil, now.Add(_leaseDuration))
                            .SetProperty(x => x.UpdatedAt, now)
                            .SetProperty(x => x.Status, JobStatus.Queued),
                    cancellationToken
                )
                .ConfigureAwait(false);

            if (affected <= 0)
            {
                continue;
            }

            cronJobOccurrence.OwnerId = owner;
            cronJobOccurrence.LockedUntil = now.Add(_leaseDuration);
            cronJobOccurrence.UpdatedAt = now;
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
                    OwnerId = owner,
                    ExecutionTime = executionTime,
                    CronJobId = item.Id,
                    LockedUntil = now.Add(_leaseDuration),
                    OnNodeDeath = item.OnNodeDeath,
                    CreatedAt = now,
                    UpdatedAt = now,
                };

                var affected = await context
                    .Upsert(itemToAdd)
                    .On(x => new { x.ExecutionTime, x.CronJobId })
                    .NoUpdate()
                    .RunAsync(cancellationToken)
                    .ConfigureAwait(false);

                if (affected <= 0)
                {
                    continue;
                }

                itemToAdd.CronJob = MappingExtensions.ProjectCronJob<TCronJob>(item, owner);
                yield return itemToAdd;
                continue;
            }

            var affectedUpdate = await context
                .Where(x => x.Id == item.NextCronOccurrence.Id)
                .Where(x => x.ExecutionTime == executionTime)
                .WhereCanAcquire(owner, now)
                .ExecuteUpdateAsync(
                    prop =>
                        prop.SetProperty(y => y.OwnerId, owner)
                            .SetProperty(y => y.LockedUntil, now.Add(_leaseDuration))
                            .SetProperty(y => y.UpdatedAt, now)
                            .SetProperty(y => y.Status, JobStatus.Queued)
                            .SetProperty(y => y.OnNodeDeath, item.OnNodeDeath),
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
                OwnerId = owner,
                LockedUntil = now.Add(_leaseDuration),
                OnNodeDeath = item.OnNodeDeath,
                UpdatedAt = now,
                CreatedAt = item.NextCronOccurrence.CreatedAt,
                CronJob = MappingExtensions.ProjectCronJob<TCronJob>(item, owner),
            };
        }
    }

    private Task<int> _ClaimTimeJobTreeAsync(
        DbSet<TTimeJob> context,
        IQueryable<TTimeJob> rootMatches,
        Guid rootId,
        string owner,
        DateTime now,
        CancellationToken cancellationToken
    )
    {
        var directChildIds = context.Where(x => x.ParentId == rootId).Select(x => x.Id);

        return context
            .Where(_ => rootMatches.Any())
            .Where(x =>
                x.Id == rootId
                || x.ParentId == rootId
                || (x.ParentId != null && directChildIds.Contains(x.ParentId.Value))
            )
            .Where(x => x.Id == rootId || x.Status == JobStatus.Idle)
            .ExecuteUpdateAsync(
                setter =>
                    setter
                        .SetProperty(x => x.OwnerId, owner)
                        .SetProperty(x => x.LockedUntil, now.Add(_leaseDuration))
                        .SetProperty(x => x.UpdatedAt, now)
                        .SetProperty(x => x.Status, x => x.Id == rootId ? JobStatus.Queued : x.Status),
                cancellationToken
            );
    }
}
