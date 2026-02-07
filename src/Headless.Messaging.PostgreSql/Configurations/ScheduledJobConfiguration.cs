// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Headless.Messaging.PostgreSql.Configurations;

/// <summary>
/// EF Core entity configuration for <see cref="ScheduledJob"/>.
/// Maps to the <c>scheduled_jobs</c> table with PostgreSQL-specific column types and partial indexes.
/// </summary>
public sealed class ScheduledJobConfiguration(string schema = PostgreSqlEntityFrameworkMessagingOptions.DefaultSchema)
    : IEntityTypeConfiguration<ScheduledJob>
{
    public void Configure(EntityTypeBuilder<ScheduledJob> builder)
    {
        builder.HasKey(x => x.Id);

        builder.Property(x => x.Id).ValueGeneratedNever();

        builder.Property(x => x.Name)
            .IsRequired()
            .HasMaxLength(200);

        builder.HasIndex(x => x.Name)
            .IsUnique()
            .HasDatabaseName("ix_scheduled_jobs_name");

        builder.Property(x => x.Type)
            .IsRequired()
            .HasConversion<string>()
            .HasMaxLength(20);

        builder.Property(x => x.CronExpression)
            .HasMaxLength(100);

        builder.Property(x => x.TimeZone)
            .IsRequired()
            .HasMaxLength(100)
            .HasDefaultValue("UTC");

        builder.Property(x => x.Payload)
            .HasColumnType("text");

        builder.Property(x => x.Status)
            .IsRequired()
            .HasConversion<string>()
            .HasMaxLength(50);

        builder.Property(x => x.NextRunTime)
            .HasColumnType("timestamptz");

        builder.Property(x => x.LastRunTime)
            .HasColumnType("timestamptz");

        builder.Property(x => x.RetryCount)
            .IsRequired()
            .HasDefaultValue(0);

        builder.Property(x => x.SkipIfRunning)
            .IsRequired()
            .HasDefaultValue(true);

        builder.Property(x => x.LockHolder)
            .HasMaxLength(256);

        builder.Property(x => x.LockedAt)
            .HasColumnType("timestamptz");

        builder.Property(x => x.IsEnabled)
            .IsRequired()
            .HasDefaultValue(true);

        builder.Property(x => x.DateCreated)
            .IsRequired()
            .HasColumnType("timestamptz");

        builder.Property(x => x.DateUpdated)
            .IsRequired()
            .HasColumnType("timestamptz");

        // Partial index: pending + enabled jobs eligible for next run pickup
        builder.HasIndex(x => x.NextRunTime)
            .HasDatabaseName("ix_scheduled_jobs_next_run")
            .HasFilter("\"Status\" IN ('Pending') AND \"IsEnabled\" = true");

        // Partial index: running jobs for lock management
        builder.HasIndex(x => new { x.LockHolder, x.LockedAt })
            .HasDatabaseName("ix_scheduled_jobs_lock")
            .HasFilter("\"Status\" = 'Running'");

        builder.ToTable("scheduled_jobs", schema);
    }
}
