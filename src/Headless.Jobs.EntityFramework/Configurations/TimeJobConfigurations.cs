// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Headless.Jobs;

/// <summary>EF Core mapping of the Jobs time-job table.</summary>
/// <param name="schema">The schema that holds the table.</param>
/// <param name="style">
/// The naming style of the database the model targets: <see cref="StorageNamingStyle.SnakeCase"/> on PostgreSQL and
/// <see cref="StorageNamingStyle.PascalCase"/> elsewhere. Pass <c>HeadlessStorageNaming.ForProvider(Database.ProviderName)</c>
/// so the model matches the names the Jobs runtime expects on that database.
/// </param>
/// <param name="contractCollation">The ordinal collation for the identity columns, or <see langword="null"/>.</param>
public class TimeJobConfigurations<TTimeJob>(string schema, StorageNamingStyle style, string? contractCollation = null)
    : IEntityTypeConfiguration<TTimeJob>
    where TTimeJob : TimeJobEntity<TTimeJob>, new()
{
    public void Configure(EntityTypeBuilder<TTimeJob> builder)
    {
        // Scheduler bookkeeping, not business data: an application context that audits by default would otherwise
        // write audit rows for every claim, heartbeat, and sweep.
        builder.ExcludeFromAudit();

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

        var table = JobsStorageNaming.Table(style, JobsStorageNaming.TimeJobs);

        builder.HasKey(x => x.Id).HasName(HeadlessStorageNaming.PrimaryKeyName(style, table));

        builder.Property(x => x.OwnerId).IsRequired(false);

        builder.Property(x => x.ExecutionTime).IsRequired(false);

        builder.Property(x => x.TenantId).IsRequired(false).HasMaxLength(JobsTenancyOptions.TenantIdMaxLength);

        builder.Property(x => x.BusinessKey).HasMaxLength(JobKey.MaxLength);
        builder.Property(x => x.IntentFingerprint).HasMaxLength(64);
        builder.Property(x => x.FingerprintAlgorithm).HasMaxLength(16);
        if (contractCollation is not null)
        {
            builder.Property(x => x.BusinessKey).UseCollation(contractCollation);
            builder.Property(x => x.TenantId).UseCollation(contractCollation);
        }

        // Transient schedule-time authorization flag: never a column.
        builder.Ignore(x => x.IsSystemJob);

        builder.Property(x => x.CancelRequested).IsRequired().HasDefaultValue(value: false);

        // Persist enums by name (not ordinal) so the stored value is stable and self-describing, and reordering
        // an enum never silently remaps existing rows. Matches Headless.Messaging's StatusName-as-string storage.
        builder.Property(x => x.Status).HasConversion<string>().HasMaxLength(32);
        builder.Property(x => x.OnNodeDeath).HasConversion<string>().HasMaxLength(32);
        builder.Property(x => x.RunCondition).HasConversion<string>().HasMaxLength(32);

        builder
            .HasOne(x => x.Parent)
            .WithMany(x => x.Children)
            .HasForeignKey(x => x.ParentId)
            .OnDelete(DeleteBehavior.NoAction)
            .HasConstraintName(JobsStorageNaming.Name(style, "FK", table, JobsStorageNaming.TimeJobs, "ParentId"));

        builder.HasIndex("ParentId").HasDatabaseName(HeadlessStorageNaming.IndexName(style, table, "ParentId"));

        builder
            .HasIndex("ExecutionTime")
            .HasDatabaseName(HeadlessStorageNaming.IndexName(style, table, "ExecutionTime"));

        // Index for scheduler queries: many jobs can share the same status/time
        builder
            .HasIndex("Status", "ExecutionTime")
            .HasDatabaseName(HeadlessStorageNaming.IndexName(style, table, "Status", "ExecutionTime"));

        // Tenant-scoped scheduler queries filter on TenantId alongside status/time.
        builder
            .HasIndex("TenantId", "Status", "ExecutionTime")
            .HasDatabaseName(HeadlessStorageNaming.IndexName(style, table, "TenantId", "Status", "ExecutionTime"));

        // Sweep/reclaim queries filter on lease deadline (Status + LockedUntil) and on ownership
        // (OwnerId + non-terminal Status); without these the 30s fallback sweep and dead-node reclaim
        // scan every InProgress/owned row.
        builder
            .HasIndex("Status", "LockedUntil")
            .HasDatabaseName(HeadlessStorageNaming.IndexName(style, table, "Status", "LockedUntil"));

        builder
            .HasIndex("OwnerId", "Status")
            .HasDatabaseName(HeadlessStorageNaming.IndexName(style, table, "OwnerId", "Status"));

        builder.ToTable(table, schema);
        builder.ApplyJobsColumnNaming(style);
    }
}
