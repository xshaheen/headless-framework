// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.EntityFramework;
using Headless.Hosting;
using Headless.Jobs.Internal;
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
        // Scheduler bookkeeping, not business data: an application context that audits by default would otherwise
        // write audit rows for every claim, heartbeat, and sweep.
        builder.ExcludeFromAudit();

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
        builder.Property(x => x.ProgressMessage).HasMaxLength(JobProgress.MessageMaxLength);
        builder.Property(x => x.TraceParent).HasMaxLength(JobTraceParent.MaxLength);

        var table = JobsStorageNaming.Table(style, JobsStorageNaming.CronJobOccurrences);

        builder.HasKey("Id").HasName(HeadlessStorageNaming.PrimaryKeyName(style, table));

        builder.Property(e => e.Id).ValueGeneratedNever();

        // The lease is a fenced row; see TimeJobConfigurations.
        builder.HasRowLease(x => x.Lease, JobsStorageNaming.LeaseColumns(style));

        builder.Property(x => x.ExecutionTime).HasConversion(utcDateTimeConverter);

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

        // Failures group by cause per function: the dashboard counts and lists failed runs by this key.
        builder.Property(x => x.FailureFingerprint).HasMaxLength(JobFailureFingerprints.MaxLength);
        builder
            .HasIndex("Function", "FailureFingerprint", "Status")
            .HasDatabaseName(HeadlessStorageNaming.IndexName(style, table, "Function", "FailureFingerprint", "Status"));

        // The dashboard's run list sorted by function pages on (Function, Id), so each page seeks instead of sorting.
        builder
            .HasIndex("Function", "Id")
            .HasDatabaseName(HeadlessStorageNaming.IndexName(style, table, "Function", "Id"));

        // The dashboard's Trace view lists every run of one correlation id.
        builder.Property(x => x.CorrelationId).HasMaxLength(JobContract.CorrelationIdMaxLength);
        builder
            .HasIndex("CorrelationId")
            .HasDatabaseName(HeadlessStorageNaming.IndexName(style, table, "CorrelationId"));

        JobsChangeMarkerModelConfiguration.ConfigureColumn(builder, style, table);

        builder.ToTable(table, schema);
        builder.ApplyJobsColumnNaming(style);
    }
}
