using Headless.Jobs.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Headless.Jobs.Configurations;

public class CronJobOccurrenceConfigurations<TCronJob>(string schema = Constants.DefaultSchema)
    : IEntityTypeConfiguration<CronJobOccurrenceEntity<TCronJob>>
    where TCronJob : CronJobEntity
{
    public void Configure(EntityTypeBuilder<CronJobOccurrenceEntity<TCronJob>> builder)
    {
        builder.HasKey("Id");

        builder.Property(e => e.Id).ValueGeneratedNever();

        builder.Property(x => x.LockHolder).IsRequired(false);

        builder.HasIndex("CronJobId").HasDatabaseName("IX_CronJobOccurrence_CronJobId");

        builder.HasIndex("ExecutionTime").HasDatabaseName("IX_CronJobOccurrence_ExecutionTime");

        builder.HasIndex("Status", "ExecutionTime").HasDatabaseName("IX_CronJobOccurrence_Status_ExecutionTime");

        builder
            .HasOne(x => x.CronJob)
            .WithMany()
            .HasForeignKey(x => x.CronJobId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex("CronJobId", "ExecutionTime").IsUnique().HasDatabaseName("UQ_CronJobId_ExecutionTime");

        builder.ToTable("CronJobOccurrences", schema);
    }
}
