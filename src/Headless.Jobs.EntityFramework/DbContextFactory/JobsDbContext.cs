using Headless.Jobs.Configurations;
using Headless.Jobs.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;

namespace Headless.Jobs.DbContextFactory;

public class JobsDbContext<TTimeJob, TCronJob> : DbContext
    where TTimeJob : TimeJobEntity<TTimeJob>, new()
    where TCronJob : CronJobEntity, new()
{
    public JobsDbContext(DbContextOptions<JobsDbContext<TTimeJob, TCronJob>> options)
        : base(options) { }

    protected JobsDbContext(DbContextOptions options)
        : base(options) { }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        var schema = this.GetService<JobsEfCoreOptionBuilder<TTimeJob, TCronJob>>().Schema;

        modelBuilder.ApplyConfiguration(new TimeJobConfigurations<TTimeJob>(schema));
        modelBuilder.ApplyConfiguration(new CronJobConfigurations<TCronJob>(schema));
        modelBuilder.ApplyConfiguration(new CronJobOccurrenceConfigurations<TCronJob>(schema));
        base.OnModelCreating(modelBuilder);
    }
}

public class JobsDbContext(DbContextOptions<JobsDbContext> options)
    : JobsDbContext<TimeJobEntity, CronJobEntity>(options);
