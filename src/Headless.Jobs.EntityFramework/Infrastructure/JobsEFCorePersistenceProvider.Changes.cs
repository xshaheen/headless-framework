// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;
using Microsoft.EntityFrameworkCore;

namespace Headless.Jobs.Infrastructure;

// Change reads over the ChangedAt marker every write stamps, and the after-commit re-stamp of writes enlisted in a
// caller's unit of work.
internal sealed partial class JobsEfCorePersistenceProvider<TDbContext, TTimeJob, TCronJob>
    where TDbContext : DbContext
    where TTimeJob : TimeJobEntity<TTimeJob>, new()
    where TCronJob : CronJobEntity, new()
{
    public async Task<DateTime?> GetLatestChangeMarkerAsync(CancellationToken cancellationToken = default)
    {
        await using var dbContext = await DbContextFactory
            .CreateDbContextAsync(cancellationToken)
            .ConfigureAwait(false);

        // Three index seeks on (ChangedAt, Id); the nullable casts make an empty table read as null.
        DateTime?[] latest =
        [
            await dbContext
                .Set<TTimeJob>()
                .MaxAsync(x => (DateTime?)x.ChangedAt, cancellationToken)
                .ConfigureAwait(false),
            await dbContext
                .Set<TCronJob>()
                .MaxAsync(x => (DateTime?)x.ChangedAt, cancellationToken)
                .ConfigureAwait(false),
            await dbContext
                .Set<CronJobOccurrenceEntity<TCronJob>>()
                .MaxAsync(x => (DateTime?)x.ChangedAt, cancellationToken)
                .ConfigureAwait(false),
        ];

        // An aggregate bypasses the column's value converter, so the kind is restored here.
        return latest.Max() is { } value ? DateTime.SpecifyKind(value, DateTimeKind.Utc) : null;
    }

    public async Task<IReadOnlyList<JobChange>> ReadChangedRowsAsync(
        JobChangeKind kind,
        DateTime after,
        DateTime through,
        JobChangePosition? resumeAfter,
        int maxCount,
        CancellationToken cancellationToken = default
    )
    {
        Argument.IsPositive(maxCount);

        await using var dbContext = await DbContextFactory
            .CreateDbContextAsync(cancellationToken)
            .ConfigureAwait(false);

        var lower = DateTime.SpecifyKind(after, DateTimeKind.Utc);
        var upper = DateTime.SpecifyKind(through, DateTimeKind.Utc);
        // The keyset arm is added only when the pass has a position, so a pass's first page is a plain range seek.
        var hasPosition = resumeAfter is not null;
        var positionAt = DateTime.SpecifyKind(resumeAfter?.ChangedAt ?? default, DateTimeKind.Utc);
        var positionId = resumeAfter?.Id ?? Guid.Empty;

        return kind switch
        {
            JobChangeKind.TimeJob => await _PageAsync(
                    _Range(dbContext.Set<TTimeJob>().AsNoTracking(), lower, upper, hasPosition, positionAt, positionId)
                        .Select(x => new JobChange
                        {
                            Kind = JobChangeKind.TimeJob,
                            Id = x.Id,
                            ChangedAt = x.ChangedAt,
                        }),
                    maxCount,
                    cancellationToken
                )
                .ConfigureAwait(false),
            JobChangeKind.CronJob => await _PageAsync(
                    _Range(dbContext.Set<TCronJob>().AsNoTracking(), lower, upper, hasPosition, positionAt, positionId)
                        .Select(x => new JobChange
                        {
                            Kind = JobChangeKind.CronJob,
                            Id = x.Id,
                            ChangedAt = x.ChangedAt,
                        }),
                    maxCount,
                    cancellationToken
                )
                .ConfigureAwait(false),
            JobChangeKind.CronJobOccurrence => await _PageAsync(
                    _OccurrenceRange(
                            dbContext.Set<CronJobOccurrenceEntity<TCronJob>>().AsNoTracking(),
                            lower,
                            upper,
                            hasPosition,
                            positionAt,
                            positionId
                        )
                        .Select(x => new JobChange
                        {
                            Kind = JobChangeKind.CronJobOccurrence,
                            Id = x.Id,
                            ChangedAt = x.ChangedAt,
                        }),
                    maxCount,
                    cancellationToken
                )
                .ConfigureAwait(false),
            _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "Unknown change kind."),
        };
    }

    private static IQueryable<TRow> _Range<TRow>(
        IQueryable<TRow> rows,
        DateTime lower,
        DateTime upper,
        bool hasPosition,
        DateTime positionAt,
        Guid positionId
    )
        where TRow : BaseJobEntity
    {
        var query = rows.Where(x => x.ChangedAt > lower && x.ChangedAt <= upper);

        if (hasPosition)
        {
            query = query.Where(x =>
                x.ChangedAt > positionAt || (x.ChangedAt == positionAt && x.Id.CompareTo(positionId) > 0)
            );
        }

        return query.OrderBy(x => x.ChangedAt).ThenBy(x => x.Id);
    }

    private static IQueryable<CronJobOccurrenceEntity<TCronJob>> _OccurrenceRange(
        IQueryable<CronJobOccurrenceEntity<TCronJob>> rows,
        DateTime lower,
        DateTime upper,
        bool hasPosition,
        DateTime positionAt,
        Guid positionId
    )
    {
        var query = rows.Where(x => x.ChangedAt > lower && x.ChangedAt <= upper);

        if (hasPosition)
        {
            query = query.Where(x =>
                x.ChangedAt > positionAt || (x.ChangedAt == positionAt && x.Id.CompareTo(positionId) > 0)
            );
        }

        return query.OrderBy(x => x.ChangedAt).ThenBy(x => x.Id);
    }

    private static async Task<IReadOnlyList<JobChange>> _PageAsync(
        IQueryable<JobChange> changes,
        int maxCount,
        CancellationToken cancellationToken
    )
    {
        return await changes.Take(maxCount).ToArrayAsync(cancellationToken).ConfigureAwait(false);
    }

    // A write enlisted in the caller's transaction was stamped when it ran, which can be long before the caller
    // committed; by then another node's change read may have passed that stamp. Re-stamping after commit, autonomously
    // and in one statement per table, moves the rows to the current end of the change stream.
    async Task ICoordinatedJobWriter<TTimeJob, TCronJob>.RestampChangesAsync(
        JobChangeKind kind,
        Guid[] ids,
        CancellationToken cancellationToken
    )
    {
        if (ids.Length == 0)
        {
            return;
        }

        await using var dbContext = await DbContextFactory
            .CreateDbContextAsync(cancellationToken)
            .ConfigureAwait(false);

        _ = kind switch
        {
            JobChangeKind.TimeJob => await dbContext
                .Set<TTimeJob>()
                .Where(x => ids.Contains(x.Id))
                .ExecuteUpdateAsync(setters => setters.SetChangedAt(), cancellationToken)
                .ConfigureAwait(false),
            JobChangeKind.CronJob => await dbContext
                .Set<TCronJob>()
                .Where(x => ids.Contains(x.Id))
                .ExecuteUpdateAsync(setters => setters.SetChangedAt(), cancellationToken)
                .ConfigureAwait(false),
            JobChangeKind.CronJobOccurrence => await dbContext
                .Set<CronJobOccurrenceEntity<TCronJob>>()
                .Where(x => ids.Contains(x.Id))
                .ExecuteUpdateAsync(setters => setters.SetChangedAt(), cancellationToken)
                .ConfigureAwait(false),
            _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "Unknown change kind."),
        };
    }
}
