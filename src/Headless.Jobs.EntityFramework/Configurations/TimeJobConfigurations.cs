// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Jobs.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Headless.Jobs.Configurations;

public class TimeJobConfigurations<TTimeJob>(string schema = Constants.DefaultSchema)
    : IEntityTypeConfiguration<TTimeJob>
    where TTimeJob : TimeJobEntity<TTimeJob>, new()
{
    public void Configure(EntityTypeBuilder<TTimeJob> builder)
    {
        builder.HasKey(x => x.Id);

        builder.Property(x => x.OwnerId).IsRequired(false);

        builder.Property(x => x.ExecutionTime).IsRequired(false);

        // Persist enums by name (not ordinal) so the stored value is stable and self-describing, and reordering
        // an enum never silently remaps existing rows. Matches Headless.Messaging's StatusName-as-string storage.
        builder.Property(x => x.Status).HasConversion<string>().HasMaxLength(32);
        builder.Property(x => x.OnNodeDeath).HasConversion<string>().HasMaxLength(32);
        builder.Property(x => x.RunCondition).HasConversion<string>().HasMaxLength(32);

        builder
            .HasOne(x => x.Parent)
            .WithMany(x => x.Children)
            .HasForeignKey(x => x.ParentId)
            .OnDelete(DeleteBehavior.NoAction);

        builder.HasIndex("ExecutionTime").HasDatabaseName("IX_TimeJob_ExecutionTime");

        // Index for scheduler queries: many jobs can share the same status/time
        builder.HasIndex("Status", "ExecutionTime").HasDatabaseName("IX_TimeJob_Status_ExecutionTime");

        // Sweep/reclaim queries filter on lease deadline (Status + LockedUntil) and on ownership
        // (OwnerId + non-terminal Status); without these the 30s fallback sweep and dead-node reclaim
        // scan every InProgress/owned row.
        builder.HasIndex("Status", "LockedUntil").HasDatabaseName("IX_TimeJob_Status_LockedUntil");

        builder.HasIndex("OwnerId", "Status").HasDatabaseName("IX_TimeJob_OwnerId_Status");

        builder.ToTable("TimeJobs", schema);
    }
}
