using Headless.Jobs.Configurations;
using Headless.Jobs.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;

namespace Headless.Jobs.DbContextFactory;

public class JobsDbContext<TTimeTicker, TCronTicker> : DbContext
    where TTimeTicker : TimeJobEntity<TTimeTicker>, new()
    where TCronTicker : CronJobEntity, new()
{
    public JobsDbContext(DbContextOptions<JobsDbContext<TTimeTicker, TCronTicker>> options)
        : base(options) { }

    protected JobsDbContext(DbContextOptions options)
        : base(options) { }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        var schema = this.GetService<JobsEfCoreOptionBuilder<TTimeTicker, TCronTicker>>().Schema;

        modelBuilder.ApplyConfiguration(new TimeJobConfigurations<TTimeTicker>(schema));
        modelBuilder.ApplyConfiguration(new CronJobConfigurations<TCronTicker>(schema));
        modelBuilder.ApplyConfiguration(new CronJobOccurrenceConfigurations<TCronTicker>(schema));
        base.OnModelCreating(modelBuilder);
    }
}

public class JobsDbContext(DbContextOptions<JobsDbContext> options)
    : JobsDbContext<TimeJobEntity, CronJobEntity>(options);
