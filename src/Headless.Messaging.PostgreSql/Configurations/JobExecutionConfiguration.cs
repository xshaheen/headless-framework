// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Headless.Messaging.PostgreSql.Configurations;

/// <summary>
/// EF Core entity configuration for <see cref="JobExecution"/>.
/// Maps to the <c>job_executions</c> table with a CASCADE FK to <c>scheduled_jobs</c>.
/// </summary>
public sealed class JobExecutionConfiguration(string schema = PostgreSqlEntityFrameworkMessagingOptions.DefaultSchema)
    : IEntityTypeConfiguration<JobExecution>
{
    public void Configure(EntityTypeBuilder<JobExecution> builder)
    {
        builder.HasKey(x => x.Id);

        builder.Property(x => x.Id).ValueGeneratedNever();

        builder.Property(x => x.JobId).IsRequired();

        builder.Property(x => x.ScheduledTime).IsRequired().HasColumnType("timestamptz");

        builder.Property(x => x.StartedAt).HasColumnType("timestamptz");

        builder.Property(x => x.CompletedAt).HasColumnType("timestamptz");

        builder.Property(x => x.Status).IsRequired().HasConversion<string>().HasMaxLength(50);

        builder.Property(x => x.Duration).HasColumnType("bigint");

        builder.Property(x => x.RetryAttempt).IsRequired().HasDefaultValue(0);

        builder.Property(x => x.Error).HasColumnType("text");

        builder.HasOne<ScheduledJob>().WithMany().HasForeignKey(x => x.JobId).OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(x => x.JobId).HasDatabaseName("ix_job_executions_job_id");

        builder.ToTable("job_executions", schema);
    }
}
