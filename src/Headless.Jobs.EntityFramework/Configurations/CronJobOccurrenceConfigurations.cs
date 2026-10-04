// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.EntityFramework.Configurations;
using Headless.Hosting.Initialization;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Headless.Jobs;

/// <summary>EF Core mapping of the Jobs cron-occurrence table.</summary>
/// <param name="schema">The schema that holds the table.</param>
/// <param name="style">
/// The naming style of the database the model targets: <see cref="StorageNamingStyle.SnakeCase"/> on PostgreSQL and
/// <see cref="StorageNamingStyle.PascalCase"/> elsewhere. Pass <c>HeadlessStorageNaming.ForProvider(Database.ProviderName)</c>
/// so the model matches the names the Jobs runtime expects on that database.
/// </param>
/// <param name="contractCollation">The ordinal collation for the identity columns, or <see langword="null"/>.</param>
public class CronJobOccurrenceConfigurations<TCronJob>(
    string schema,
    StorageNamingStyle style,
    string? contractCollation = null
) : IEntityTypeConfiguration<CronJobOccurrenceEntity<TCronJob>>
    where TCronJob : CronJobEntity
{
    public void Configure(EntityTypeBuilder<CronJobOccurrenceEntity<TCronJob>> builder)
    {
        var utcDateTimeConverter = new NormalizeDateTimeValueConverter();
        var nullableUtcDateTimeConverter = new NullableNormalizeDateTimeValueConverter();

        builder
            .Property(x => x.Function)
            .IsRequired()
            .HasMaxLength(JobContract.NameMaxLength)
            .HasConversion(value => JobContract.ValidateName(value), value => value);
        builder
            .Property(x => x.ContractVersion)
            .IsRequired()
            .HasMaxLength(JobContract.VersionMaxLength)
            .HasConversion(value => JobContract.ValidateVersion(value), value => value);
        if (contractCollation is not null)
        {
            builder.Property(x => x.Function).UseCollation(contractCollation);
            builder.Property(x => x.ContractVersion).UseCollation(contractCollation);
        }

        builder.Property(x => x.TenantId).HasMaxLength(JobsTenancyOptions.TenantIdMaxLength);

        var table = JobsStorageNaming.Table(style, JobsStorageNaming.CronJobOccurrences);

        builder.HasKey("Id").HasName(HeadlessStorageNaming.PrimaryKeyName(style, table));

        builder.Property(e => e.Id).ValueGeneratedNever();

        builder.Property(x => x.OwnerId).IsRequired(false);

        builder.Property(x => x.ExecutionTime).HasConversion(utcDateTimeConverter);
        builder.Property(x => x.LockedUntil).HasConversion(nullableUtcDateTimeConverter);

        builder.Property(x => x.RecoveredFromUtc).HasConversion(nullableUtcDateTimeConverter);

        // Persist enums by name (not ordinal) — see TimeJobConfigurations.
        builder.Property(x => x.Status).HasConversion<string>().HasMaxLength(32);
        builder.Property(x => x.OnNodeDeath).HasConversion<string>().HasMaxLength(32);

        // The occupied-instant rule's sole accounting input, compared as a string in every provider's SQL so an
        // unrecognized value can never throw on the read path — see CronOccurrenceAccounting.
        builder.Property(x => x.Disposition).HasConversion<string>().HasMaxLength(32);

        // Derived from RecoveredFromUtc so the two cannot disagree; never a column.
        builder.Ignore(x => x.IsRecoveryRun);

        builder.HasIndex("CronJobId").HasDatabaseName(HeadlessStorageNaming.IndexName(style, table, "CronJobId"));

        builder
            .HasIndex("ExecutionTime")
            .HasDatabaseName(HeadlessStorageNaming.IndexName(style, table, "ExecutionTime"));

        builder
            .HasIndex("Status", "ExecutionTime")
            .HasDatabaseName(HeadlessStorageNaming.IndexName(style, table, "Status", "ExecutionTime"));

        // Sweep/reclaim queries filter on lease deadline (Status + LockedUntil) and on ownership
        // (OwnerId + non-terminal Status) — see TimeJobConfigurations.
        builder
            .HasIndex("Status", "LockedUntil")
            .HasDatabaseName(HeadlessStorageNaming.IndexName(style, table, "Status", "LockedUntil"));

        builder
            .HasIndex("OwnerId", "Status")
            .HasDatabaseName(HeadlessStorageNaming.IndexName(style, table, "OwnerId", "Status"));

        builder
            .HasOne(x => x.CronJob)
            .WithMany()
            .HasForeignKey(x => x.CronJobId)
            .OnDelete(DeleteBehavior.Cascade)
            .HasConstraintName(JobsStorageNaming.Name(style, "FK", table, JobsStorageNaming.CronJobs, "CronJobId"));

        builder
            .HasIndex("CronJobId", "ExecutionTime")
            .IsUnique()
            .HasFilter($"{JobsStorageNaming.QuotedColumn(style, "Status")} IN ('Idle', 'Queued', 'InProgress')")
            .HasDatabaseName(JobsStorageNaming.Name(style, "UQ", table, "CronJobId", "ExecutionTime"));

        builder.ToTable(table, schema);
        builder.ApplyJobsColumnNaming(style);
    }
}
