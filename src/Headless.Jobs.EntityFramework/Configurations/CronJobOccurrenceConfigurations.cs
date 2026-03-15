using Headless.Jobs.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Headless.Jobs.Configurations;

public class CronJobOccurrenceConfigurations<TCronTicker>(string schema = Constants.DefaultSchema)
    : IEntityTypeConfiguration<CronJobOccurrenceEntity<TCronTicker>>
    where TCronTicker : CronJobEntity
{
    public void Configure(EntityTypeBuilder<CronJobOccurrenceEntity<TCronTicker>> builder)
    {
        builder.HasKey("Id");

        builder.Property(e => e.Id).ValueGeneratedNever();

        builder.Property(x => x.LockHolder).IsRequired(false);

        builder.HasIndex("CronJobId").HasDatabaseName("IX_CronTickerOccurrence_CronJobId");

        builder.HasIndex("ExecutionTime").HasDatabaseName("IX_CronTickerOccurrence_ExecutionTime");

        builder.HasIndex("Status", "ExecutionTime").HasDatabaseName("IX_CronTickerOccurrence_Status_ExecutionTime");

        builder
            .HasOne(x => x.CronTicker)
            .WithMany()
            .HasForeignKey(x => x.CronJobId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex("CronJobId", "ExecutionTime").IsUnique().HasDatabaseName("UQ_CronJobId_ExecutionTime");

        builder.ToTable("CronTickerOccurrences", schema);
    }
}
