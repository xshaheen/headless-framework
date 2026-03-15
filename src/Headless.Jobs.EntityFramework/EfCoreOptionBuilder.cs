using Headless.Jobs.Customizer;
using Headless.Jobs.DbContextFactory;
using Headless.Jobs.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Headless.Jobs;

public class JobsEfCoreOptionBuilder<TTimeTicker, TCronTicker>
    where TTimeTicker : TimeTickerEntity<TTimeTicker>, new()
    where TCronTicker : CronTickerEntity, new()
{
    internal Action<IServiceCollection> ConfigureServices { get; set; } = _ => { };
    internal int PoolSize { get; set; } = 1024;
    internal string Schema { get; set; } = "ticker";

    public JobsEfCoreOptionBuilder<TTimeTicker, TCronTicker> UseApplicationDbContext<TDbContext>(
        ConfigurationType configurationType
    )
        where TDbContext : DbContext
    {
        ServiceBuilder.UseApplicationDbContext<TDbContext, TTimeTicker, TCronTicker>(this, configurationType);
        return this;
    }

    public JobsEfCoreOptionBuilder<TTimeTicker, TCronTicker> UseJobsDbContext<TDbContext>(
        Action<DbContextOptionsBuilder> optionsAction,
        string? schema = null
    )
        where TDbContext : JobsDbContext<TTimeTicker, TCronTicker>
    {
        Schema = schema ?? Schema;

        ServiceBuilder.UseJobsDbContext<TDbContext, TTimeTicker, TCronTicker>(this, optionsAction);
        return this;
    }

    public JobsEfCoreOptionBuilder<TTimeTicker, TCronTicker> SetDbContextPoolSize(int poolSize)
    {
        PoolSize = poolSize;
        return this;
    }

    public JobsEfCoreOptionBuilder<TTimeTicker, TCronTicker> SetSchema(string schema)
    {
        Schema = schema;
        return this;
    }
}
