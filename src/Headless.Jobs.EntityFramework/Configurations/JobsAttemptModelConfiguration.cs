// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.EntityFramework;
using Headless.Hosting;
using Microsoft.EntityFrameworkCore;

namespace Headless.Jobs;

/// <summary>
/// Maps the attempt history tables: <c>TimeJobAttempts</c> and <c>CronJobOccurrenceAttempts</c>, each keyed by its run
/// and the attempt number, with a cascading foreign key to the run so deleting a run (one row, a whole chain, a cron
/// definition's occurrences, or a retired seed) deletes its attempts in the same statement.
/// </summary>
/// <remarks>
/// Two tables rather than one keyed by run kind, because a foreign key names one principal table: a shared table could
/// not cascade, and every delete path would have to remember the attempts. A model without these mappings keeps
/// working; the store then records no attempt history.
/// </remarks>
internal static class JobsAttemptModelConfiguration
{
    /// <summary>
    /// Maps both attempt tables in <paramref name="schema"/>, for the time job type <paramref name="timeJobType"/> and
    /// the cron occurrence type <paramref name="occurrenceType"/>; a <see langword="null"/> run type maps no table.
    /// </summary>
    internal static void Configure(
        ModelBuilder builder,
        Type? timeJobType,
        Type? occurrenceType,
        string schema,
        StorageNamingStyle style
    )
    {
        if (timeJobType is not null)
        {
            _Configure<TimeJobAttemptEntity>(
                builder,
                timeJobType,
                JobsStorageNaming.TimeJobAttempts,
                JobsStorageNaming.TimeJobs,
                schema,
                style
            );
        }

        if (occurrenceType is not null)
        {
            _Configure<CronJobOccurrenceAttemptEntity>(
                builder,
                occurrenceType,
                JobsStorageNaming.CronJobOccurrenceAttempts,
                JobsStorageNaming.CronJobOccurrences,
                schema,
                style
            );
        }
    }

    /// <summary>The cron occurrence entity type mapped in <paramref name="builder"/>'s model, if any.</summary>
    internal static Type? FindOccurrenceType(ModelBuilder builder)
    {
        return builder
            .Model.GetEntityTypes()
            .Select(static entityType => entityType.ClrType)
            .FirstOrDefault(static type =>
                type.IsGenericType && type.GetGenericTypeDefinition() == typeof(CronJobOccurrenceEntity<>)
            );
    }

    private static void _Configure<TAttempt>(
        ModelBuilder builder,
        Type runType,
        string pascalTable,
        string pascalRunTable,
        string schema,
        StorageNamingStyle style
    )
        where TAttempt : JobAttemptEntity
    {
        var table = JobsStorageNaming.Table(style, pascalTable);

        builder.Entity<TAttempt>(entity =>
        {
            // Scheduler bookkeeping, like the run tables: never captured by an application context's audit log.
            entity.ExcludeFromAudit();
            entity.ToTable(table, schema);

            entity
                .HasKey(attempt => new { attempt.RunId, attempt.Number })
                .HasName(HeadlessStorageNaming.PrimaryKeyName(style, table));
            entity.Property(attempt => attempt.Number).ValueGeneratedNever();

            entity.Property(attempt => attempt.StartedAt).HasConversion(new NullableNormalizeDateTimeValueConverter());
            entity.Property(attempt => attempt.EndedAt).HasConversion(new NullableNormalizeDateTimeValueConverter());

            entity.Property(attempt => attempt.Outcome).HasConversion<string>().HasMaxLength(32);
            entity.Property(attempt => attempt.FailureFingerprint).HasMaxLength(JobFailureFingerprints.MaxLength);

            entity
                .HasOne(runType)
                .WithMany()
                .HasForeignKey(nameof(JobAttemptEntity.RunId))
                .OnDelete(DeleteBehavior.Cascade)
                .HasConstraintName(JobsStorageNaming.Name(style, "FK", table, pascalRunTable, "RunId"));

            entity.ApplyJobsColumnNaming(style);
        });
    }
}
