// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Storage;

namespace Headless.Jobs;

/// <summary>The cron-occurrence indexes whose filters name the <c>Status</c> column.</summary>
/// <remarks>
/// A filter is SQL text, so it does not follow a column a consumer renames after the Jobs configuration ran. The entity
/// configuration declares these indexes with the default column name, and every model-finalizing path restates them
/// from the final column name once the consumer's mappings have settled. On PostgreSQL the cron insert's
/// <c>ON CONFLICT</c> predicate must match the unique index's predicate, so a stale name there breaks every insert.
/// </remarks>
internal static class JobsOccurrenceModelConfiguration
{
    /// <summary>Restates the status filters of <paramref name="occurrenceType"/> from its final column names.</summary>
    internal static void Configure(ModelBuilder builder, Type? occurrenceType, DbContext context)
    {
        if (occurrenceType is null)
        {
            return;
        }

        var entity = builder.Entity(occurrenceType);
        var table = StoreObjectIdentifier.Table(entity.Metadata.GetTableName()!, entity.Metadata.GetSchema());
        var status = entity.Metadata.FindProperty(nameof(CronJobOccurrenceEntity<>.Status))!.GetColumnName(table)!;

        ConfigureStatusIndexes(
            entity,
            JobsStorageNaming.StyleOf(context),
            context.GetService<ISqlGenerationHelper>().DelimitIdentifier(status)
        );
    }

    /// <summary>Declares the filtered indexes over the delimited status column <paramref name="status"/>.</summary>
    internal static void ConfigureStatusIndexes(EntityTypeBuilder builder, StorageNamingStyle style, string status)
    {
        // Named after the default table rather than its final mapping, so the names stay the same whichever path
        // declared the index last.
        var table = JobsStorageNaming.Table(style, JobsStorageNaming.CronJobOccurrences);

        // The peek and every claim filter to the not-yet-started rows and read them in (ExecutionTime, Id) order. Filtered
        // to those rows, the index stays the size of the backlog instead of the whole history, and the scan reads it in
        // order and stops at its page instead of skipping past every finished row.
        builder
            .HasIndex(nameof(CronJobOccurrenceEntity<>.ExecutionTime), nameof(CronJobOccurrenceEntity<>.Id))
            .HasFilter($"{status} IN ('Idle', 'Queued')")
            .HasDatabaseName(HeadlessStorageNaming.IndexName(style, table, "ExecutionTime", "Id", "Claimable"));

        // One live occurrence per definition and instant; finished rows drop out so a rerun can take the instant again.
        builder
            .HasIndex(nameof(CronJobOccurrenceEntity<>.CronJobId), nameof(CronJobOccurrenceEntity<>.ExecutionTime))
            .IsUnique()
            .HasFilter($"{status} IN ('Idle', 'Queued', 'InProgress')")
            .HasDatabaseName(JobsStorageNaming.Name(style, "UQ", table, "CronJobId", "ExecutionTime"));
    }
}
