using Framework.Ticker.Utilities.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Framework.Ticker.Configurations;

public class CronTickerConfigurations<TCronTicker>(string schema = Constants.DefaultSchema)
    : IEntityTypeConfiguration<TCronTicker>
    where TCronTicker : CronTickerEntity, new()
{
    public void Configure(EntityTypeBuilder<TCronTicker> builder)
    {
        builder.HasKey("Id");

        builder.Property(e => e.Id).ValueGeneratedNever();

        builder.HasIndex("Expression").HasDatabaseName("IX_CronTickers_Expression");

        // Index for common lookups by function + expression
        builder.HasIndex("Function", "Expression").HasDatabaseName("IX_Function_Expression");

        builder.ToTable("CronTickers", schema);
    }
}
