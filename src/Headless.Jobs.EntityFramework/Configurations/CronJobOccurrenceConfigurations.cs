// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.EntityFramework.Configurations;
using Headless.Jobs.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Headless.Jobs.Configurations;

public class CronJobOccurrenceConfigurations<TCronJob>(
    string schema = JobDbConstants.DefaultSchema,
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

        builder.Property(x => x.TenantId).HasMaxLength(Models.JobsTenancyOptions.TenantIdMaxLength);

        builder.HasKey("Id");

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

        builder.HasIndex("CronJobId").HasDatabaseName("IX_CronJobOccurrence_CronJobId");

        builder.HasIndex("ExecutionTime").HasDatabaseName("IX_CronJobOccurrence_ExecutionTime");

        builder.HasIndex("Status", "ExecutionTime").HasDatabaseName("IX_CronJobOccurrence_Status_ExecutionTime");

        // Sweep/reclaim queries filter on lease deadline (Status + LockedUntil) and on ownership
        // (OwnerId + non-terminal Status) — see TimeJobConfigurations.
        builder.HasIndex("Status", "LockedUntil").HasDatabaseName("IX_CronJobOccurrence_Status_LockedUntil");

        builder.HasIndex("OwnerId", "Status").HasDatabaseName("IX_CronJobOccurrence_OwnerId_Status");

        builder.HasOne(x => x.CronJob).WithMany().HasForeignKey(x => x.CronJobId).OnDelete(DeleteBehavior.Cascade);

        builder
            .HasIndex("CronJobId", "ExecutionTime")
            .IsUnique()
            .HasFilter("\"Status\" IN ('Idle', 'Queued', 'InProgress')")
            .HasDatabaseName("UQ_CronJobId_ExecutionTime");

        builder.ToTable("CronJobOccurrences", schema);
    }
}
