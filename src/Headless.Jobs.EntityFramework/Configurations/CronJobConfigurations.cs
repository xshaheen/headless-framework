// Copyright (c) Mahmoud Shaheen. All rights reserved.

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
        builder.HasKey("Id");

        builder.Property(e => e.Id).ValueGeneratedNever();

        builder.Property(e => e.IsPaused).HasDefaultValue(value: false);

        builder.Property(e => e.ScheduleRevision).HasDefaultValue(0L);

        builder.Property(e => e.TimeZoneId).HasMaxLength(128);

        builder.Property(e => e.OnNodeDeath).HasConversion<string>().HasMaxLength(32);

        builder.Property(e => e.OnMissedRun).HasConversion<string>().HasMaxLength(32);

        builder.Property(e => e.EvaluationFingerprint).HasMaxLength(128);

        // The scheduler selects due definitions by this column instead of evaluating every expression on every
        // node, so it carries the dispatch hot path and is indexed alongside the pause flag it is always filtered
        // with. The fingerprint sweep selects on staleness independently of due-ness, hence the second index.
        builder
            .HasIndex(nameof(CronJobEntity.IsPaused), nameof(CronJobEntity.NextDueUtc))
            .HasDatabaseName("IX_CronJobs_IsPaused_NextDueUtc");

        builder
            .HasIndex(nameof(CronJobEntity.EvaluationFingerprint))
            .HasDatabaseName("IX_CronJobs_EvaluationFingerprint");

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
