// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Jobs.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Headless.Jobs.Configurations;

public class CronJobConfigurations<TCronJob>(string schema = JobDbConstants.DefaultSchema)
    : IEntityTypeConfiguration<TCronJob>
    where TCronJob : CronJobEntity, new()
{
    public void Configure(EntityTypeBuilder<TCronJob> builder)
    {
        var utcDateTimeConverter = new JobsUtcDateTimeValueConverter();

        builder.HasKey("Id");

        builder.Property(e => e.Id).ValueGeneratedNever();

        builder.Property(e => e.IsPaused).HasDefaultValue(value: false);

        builder.Property(e => e.ScheduleRevision).HasDefaultValue(0L);

        builder.Property(e => e.TimeZoneId).HasMaxLength(128);

        builder.Property(e => e.CreatedAt).HasConversion(utcDateTimeConverter);

        builder.Property(e => e.UpdatedAt).HasConversion(utcDateTimeConverter);

        builder.Property(e => e.OnNodeDeath).HasConversion<string>().HasMaxLength(32);

        builder.HasIndex("Expression").HasDatabaseName("IX_CronJobs_Expression");

        // Index for common lookups by function + expression
        builder.HasIndex("Function", "Expression").HasDatabaseName("IX_Function_Expression");

        builder.ToTable("CronJobs", schema);
    }
}
