// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Microsoft.EntityFrameworkCore;

#pragma warning disable MA0133 // EF must keep DateTime.UtcNow in expression trees so providers translate the database clock before the DateTimeOffset assignment.
namespace Headless.Jobs.Infrastructure;

// Operator requeue of a Failed cron occurrence for the relational provider. It holds the definition row lock every
// occurrence producer takes first, so the overlap and instant checks stay true until the write commits.
internal sealed partial class JobsEfCorePersistenceProvider<TDbContext, TTimeJob, TCronJob>
    where TDbContext : DbContext
    where TTimeJob : TimeJobEntity<TTimeJob>, new()
    where TCronJob : CronJobEntity, new()
{
    public async Task<JobRequeueOutcome> RequeueCronJobOccurrenceAsync(
        Guid occurrenceId,
        CancellationToken cancellationToken = default
    )
    {
        await using var dbContext = await DbContextFactory
            .CreateDbContextAsync(cancellationToken)
            .ConfigureAwait(false);
        var occurrences = dbContext.Set<CronJobOccurrenceEntity<TCronJob>>();

        var observed = await occurrences
            .AsNoTracking()
            .Where(x => x.Id == occurrenceId)
            .Select(x => new
            {
                x.Status,
                x.CronJobId,
                x.ExecutionTime,
            })
            .SingleOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);

        if (observed is null)
        {
            return JobRequeueOutcome.NotFound;
        }

        if (observed.Status != JobStatus.Failed)
        {
            return JobRequeueOutcome.NotFailed;
        }

        await using var transaction = await dbContext
            .Database.BeginTransactionAsync(cancellationToken)
            .ConfigureAwait(false);

        // Take the definition row lock that every occurrence producer takes first (materialization, recovery, pause,
        // resume, schedule edit). A no-op write is enough to hold it through commit, so no new occurrence of this
        // definition can appear between the checks below and the write.
        var cronJobId = observed.CronJobId;
        var locked = await dbContext
            .Set<TCronJob>()
            .Where(x => x.Id == cronJobId)
            .ExecuteUpdateAsync(
                setter => setter.SetProperty(x => x.ScheduleRevision, x => x.ScheduleRevision),
                cancellationToken
            )
            .ConfigureAwait(false);

        if (locked == 0)
        {
            return JobRequeueOutcome.NotFound;
        }

        var onOverlap = await dbContext
            .Set<TCronJob>()
            .AsNoTracking()
            .Where(x => x.Id == cronJobId)
            .Select(x => x.OnOverlap)
            .SingleAsync(cancellationToken)
            .ConfigureAwait(false);

        var unfinishedSiblings = occurrences
            .Where(CronOverlapRule.UnfinishedOccurrenceOf<TCronJob>(cronJobId))
            .Where(x => x.Id != occurrenceId);

        if (
            CronOverlapRule.ForbidsOverlap(onOverlap)
            && await unfinishedSiblings.AnyAsync(cancellationToken).ConfigureAwait(false)
        )
        {
            return JobRequeueOutcome.Overlap;
        }

        // The occurrence keeps its instant, which the filtered unique index allows to only one live row.
        var executionTime = observed.ExecutionTime;
        if (
            await unfinishedSiblings
                .AnyAsync(x => x.ExecutionTime == executionTime, cancellationToken)
                .ConfigureAwait(false)
        )
        {
            return JobRequeueOutcome.Conflict;
        }

        var affected = await occurrences
            .Where(x => x.Id == occurrenceId && x.Status == JobStatus.Failed)
            .ExecuteUpdateAsync(
                setter =>
                    setter
                        .SetProperty(x => x.Status, JobStatus.Idle)
                        .SetProperty(x => x.RetryCount, 0)
                        .SetProperty(x => x.ExceptionMessage, _ => null)
                        .SetProperty(x => x.SkippedReason, _ => null)
                        .SetProperty(x => x.OwnerId, _ => null)
                        .SetProperty(x => x.LockedUntil, _ => null)
                        .SetProperty(x => x.ExecutedAt, _ => null)
                        .SetProperty(x => x.ElapsedTime, 0L)
                        // An audit stamp inside the transaction, which BasePersistenceProvider's clock invariant
                        // permits: only lease deadlines must never be written inside an explicit transaction.
                        .SetProperty(x => x.UpdatedAt, _ => DateTime.UtcNow),
                cancellationToken
            )
            .ConfigureAwait(false);

        if (affected == 0)
        {
            // Another requeue moved the row after the read above; it waited on the definition lock and lost.
            return JobRequeueOutcome.Conflict;
        }

        await transaction.CommitAsync(CancellationToken.None).ConfigureAwait(false);
        return JobRequeueOutcome.Requeued;
    }
}
