using Headless.Jobs.Configurations;
using Headless.Jobs.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;

namespace Headless.Jobs.DbContextFactory;

public class JobsDbContext<TTimeTicker, TCronTicker> : DbContext
    where TTimeTicker : TimeTickerEntity<TTimeTicker>, new()
    where TCronTicker : CronTickerEntity, new()
{
    public JobsDbContext(DbContextOptions<JobsDbContext<TTimeTicker, TCronTicker>> options)
        : base(options) { }

    protected JobsDbContext(DbContextOptions options)
        : base(options) { }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        var schema = this.GetService<JobsEfCoreOptionBuilder<TTimeTicker, TCronTicker>>().Schema;

        modelBuilder.ApplyConfiguration(new TimeTickerConfigurations<TTimeTicker>(schema));
        modelBuilder.ApplyConfiguration(new CronTickerConfigurations<TCronTicker>(schema));
        modelBuilder.ApplyConfiguration(new CronTickerOccurrenceConfigurations<TCronTicker>(schema));
        base.OnModelCreating(modelBuilder);
    }
}

public class JobsDbContext(DbContextOptions<JobsDbContext> options)
    : JobsDbContext<TimeTickerEntity, CronTickerEntity>(options);
