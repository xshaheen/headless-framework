using Headless.Ticker.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Headless.Ticker.Configurations;

public class TimeTickerConfigurations<TTimeTicker>(string schema = Constants.DefaultSchema)
    : IEntityTypeConfiguration<TTimeTicker>
    where TTimeTicker : TimeTickerEntity<TTimeTicker>, new()
{
    public void Configure(EntityTypeBuilder<TTimeTicker> builder)
    {
        builder.HasKey(x => x.Id);

        builder.Property(x => x.LockHolder).IsRequired(false);

        builder.Property(x => x.ExecutionTime).IsRequired(false);

        builder
            .HasOne(x => x.Parent)
            .WithMany(x => x.Children)
            .HasForeignKey(x => x.ParentId)
            .OnDelete(DeleteBehavior.NoAction);

        builder.HasIndex("ExecutionTime").HasDatabaseName("IX_TimeTicker_ExecutionTime");

        // Index for scheduler queries: many tickers can share the same status/time
        builder.HasIndex("Status", "ExecutionTime").HasDatabaseName("IX_TimeTicker_Status_ExecutionTime");

        builder.ToTable("TimeTickers", schema);
    }
}
