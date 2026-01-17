using Framework.Ticker.EntityFrameworkCore.Configurations;
using Framework.Ticker.Utilities.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;

namespace Framework.Ticker.EntityFrameworkCore.DbContextFactory;

public class TickerQDbContext<TTimeTicker, TCronTicker> : DbContext
    where TTimeTicker : TimeTickerEntity<TTimeTicker>, new()
    where TCronTicker : CronTickerEntity, new()
{
    public TickerQDbContext(DbContextOptions<TickerQDbContext<TTimeTicker, TCronTicker>> options)
        : base(options) { }

    protected TickerQDbContext(DbContextOptions options)
        : base(options) { }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        var schema = this.GetService<TickerQEfCoreOptionBuilder<TTimeTicker, TCronTicker>>().Schema;

        modelBuilder.ApplyConfiguration(new TimeTickerConfigurations<TTimeTicker>(schema));
        modelBuilder.ApplyConfiguration(new CronTickerConfigurations<TCronTicker>(schema));
        modelBuilder.ApplyConfiguration(new CronTickerOccurrenceConfigurations<TCronTicker>(schema));
        base.OnModelCreating(modelBuilder);
    }
}

public class TickerQDbContext : TickerQDbContext<TimeTickerEntity, CronTickerEntity>
{
    public TickerQDbContext(DbContextOptions<TickerQDbContext> options)
        : base(options) { }
}
