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

        builder.Property(x => x.LockHolder).IsRequired(false);

        builder.Property(x => x.ExecutionTime).IsRequired(false);

        builder
            .HasOne(x => x.Parent)
            .WithMany(x => x.Children)
            .HasForeignKey(x => x.ParentId)
            .OnDelete(DeleteBehavior.NoAction);

        builder.HasIndex("ExecutionTime").HasDatabaseName("IX_TimeJob_ExecutionTime");

        // Index for scheduler queries: many jobs can share the same status/time
        builder.HasIndex("Status", "ExecutionTime").HasDatabaseName("IX_TimeJob_Status_ExecutionTime");

        builder.ToTable("TimeJobs", schema);
    }
}
