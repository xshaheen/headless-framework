// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.EntityFramework.Configurations;
using Headless.Jobs.Entities;
using Headless.Jobs.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Headless.Jobs.Configurations;

public class CronJobConfigurations<TCronJob>(string schema = JobDbConstants.DefaultSchema)
    : IEntityTypeConfiguration<TCronJob>
    where TCronJob : CronJobEntity, new()
{
    public void Configure(EntityTypeBuilder<TCronJob> builder)
    {
        var utcDateTimeConverter = new NormalizeDateTimeValueConverter();

        builder.HasKey("Id");

        builder.Property(e => e.Id).ValueGeneratedNever();

        builder.Property(e => e.IsPaused).HasDefaultValue(value: false);

        builder.Property(e => e.ScheduleRevision).HasDefaultValue(0L);

        builder.Property(e => e.TimeZoneId).HasMaxLength(128);

        builder.Property(e => e.DateCreated).HasConversion(utcDateTimeConverter);

        builder.Property(e => e.DateUpdated).HasConversion(utcDateTimeConverter);

        builder.Property(e => e.OnNodeDeath).HasConversion<string>().HasMaxLength(32);

        // Cron is system-scope by contract (a tenant-scoped cron definition is rejected at schedule time), so
        // TenantId always persists null. Bound the column length for parity with time jobs; no tenant index — cron
        // pickup never filters by tenant.
        builder.Property(e => e.TenantId).IsRequired(false).HasMaxLength(JobsTenancyOptions.TenantIdMaxLength);

        // Transient schedule-time authorization flag (KTD2): never a column.
        builder.Ignore(e => e.IsSystemJob);

        builder.HasIndex("Expression").HasDatabaseName("IX_CronJobs_Expression");

        // Index for common lookups by function + expression
        builder.HasIndex("Function", "Expression").HasDatabaseName("IX_Function_Expression");

        builder.ToTable("CronJobs", schema);
    }
}
