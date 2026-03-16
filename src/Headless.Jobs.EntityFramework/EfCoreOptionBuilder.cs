using Headless.Jobs.Customizer;
using Headless.Jobs.DbContextFactory;
using Headless.Jobs.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Headless.Jobs;

public class JobsEfCoreOptionBuilder<TTimeJob, TCronJob>
    where TTimeJob : TimeJobEntity<TTimeJob>, new()
    where TCronJob : CronJobEntity, new()
{
    internal Action<IServiceCollection> ConfigureServices { get; set; } = _ => { };
    internal int PoolSize { get; set; } = 1024;
    internal string Schema { get; set; } = "jobs";

    public JobsEfCoreOptionBuilder<TTimeJob, TCronJob> UseApplicationDbContext<TDbContext>(
        ConfigurationType configurationType
    )
        where TDbContext : DbContext
    {
        ServiceBuilder.UseApplicationDbContext<TDbContext, TTimeJob, TCronJob>(this, configurationType);
        return this;
    }

    public JobsEfCoreOptionBuilder<TTimeJob, TCronJob> UseJobsDbContext<TDbContext>(
        Action<DbContextOptionsBuilder> optionsAction,
        string? schema = null
    )
        where TDbContext : JobsDbContext<TTimeJob, TCronJob>
    {
        Schema = schema ?? Schema;

        ServiceBuilder.UseJobsDbContext<TDbContext, TTimeJob, TCronJob>(this, optionsAction);
        return this;
    }

    public JobsEfCoreOptionBuilder<TTimeJob, TCronJob> SetDbContextPoolSize(int poolSize)
    {
        PoolSize = poolSize;
        return this;
    }

    public JobsEfCoreOptionBuilder<TTimeJob, TCronJob> SetSchema(string schema)
    {
        Schema = schema;
        return this;
    }
}
