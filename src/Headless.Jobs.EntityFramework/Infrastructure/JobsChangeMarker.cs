// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Query;

namespace Headless.Jobs.Infrastructure;

/// <summary>
/// The change marker every write to a Jobs row stamps, so a change read on any node sees the row.
/// </summary>
internal static class JobsChangeMarker
{
    /// <summary>
    /// The row's next change marker: the later of the database statement clock and <paramref name="previous"/> plus
    /// one tick of the column's precision. Translated by the database (see <c>JobsChangeMarkerModelConfiguration</c>);
    /// never evaluated in process.
    /// </summary>
    /// <exception cref="InvalidOperationException">Always, when called outside a query.</exception>
    public static DateTime Next(DateTime previous)
    {
        throw new InvalidOperationException(
            $"{nameof(JobsChangeMarker)}.{nameof(Next)} is translated by the database and cannot run in process "
                + $"(previous marker {previous:O})."
        );
    }

    /// <summary>Chains the change-marker stamp onto an update's setters.</summary>
    internal static UpdateSettersBuilder<TEntity> SetChangedAt<TEntity>(this UpdateSettersBuilder<TEntity> setters)
        where TEntity : BaseJobEntity
    {
        return setters.SetProperty(x => x.ChangedAt, x => Next(x.ChangedAt));
    }

    /// <summary>Chains the change-marker stamp onto a cron occurrence update's setters.</summary>
    internal static UpdateSettersBuilder<CronJobOccurrenceEntity<TCronJob>> SetChangedAt<TCronJob>(
        this UpdateSettersBuilder<CronJobOccurrenceEntity<TCronJob>> setters
    )
        where TCronJob : CronJobEntity
    {
        return setters.SetProperty(x => x.ChangedAt, x => Next(x.ChangedAt));
    }

    /// <summary>
    /// Saves <paramref name="dbContext"/>'s tracked changes, then stamps the change marker of every Jobs row the save
    /// updated.
    /// </summary>
    /// <remarks>
    /// The model never writes the marker from a tracked entity (an insert takes the column default), so a tracked
    /// update leaves it unchanged; the follow-up statement runs on the same connection and transaction as the save.
    /// </remarks>
    internal static async Task<int> SaveChangesAndStampAsync<TTimeJob, TCronJob>(
        this DbContext dbContext,
        CancellationToken cancellationToken,
        bool acceptAllChangesOnSuccess = true
    )
        where TTimeJob : TimeJobEntity<TTimeJob>, new()
        where TCronJob : CronJobEntity, new()
    {
        List<Guid>? timeJobs = null;
        List<Guid>? cronJobs = null;
        List<Guid>? occurrences = null;

        foreach (var entry in dbContext.ChangeTracker.Entries())
        {
            if (entry.State != EntityState.Modified)
            {
                continue;
            }

            switch (entry.Entity)
            {
                case TTimeJob timeJob:
                    (timeJobs ??= []).Add(timeJob.Id);
                    break;
                case TCronJob cronJob:
                    (cronJobs ??= []).Add(cronJob.Id);
                    break;
                case CronJobOccurrenceEntity<TCronJob> occurrence:
                    (occurrences ??= []).Add(occurrence.Id);
                    break;
            }
        }

        // The single-argument overload when the caller accepts changes, so an application context that overrides only
        // that overload still runs its save pipeline.
        var affected = acceptAllChangesOnSuccess
            ? await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false)
            : await dbContext
                .SaveChangesAsync(acceptAllChangesOnSuccess: false, cancellationToken)
                .ConfigureAwait(false);

        if (timeJobs is not null)
        {
            await dbContext
                .Set<TTimeJob>()
                .Where(x => timeJobs.Contains(x.Id))
                .ExecuteUpdateAsync(setters => setters.SetChangedAt(), cancellationToken)
                .ConfigureAwait(false);
        }

        if (cronJobs is not null)
        {
            await dbContext
                .Set<TCronJob>()
                .Where(x => cronJobs.Contains(x.Id))
                .ExecuteUpdateAsync(setters => setters.SetChangedAt(), cancellationToken)
                .ConfigureAwait(false);
        }

        if (occurrences is not null)
        {
            await dbContext
                .Set<CronJobOccurrenceEntity<TCronJob>>()
                .Where(x => occurrences.Contains(x.Id))
                .ExecuteUpdateAsync(setters => setters.SetChangedAt(), cancellationToken)
                .ConfigureAwait(false);
        }

        return affected;
    }
}
