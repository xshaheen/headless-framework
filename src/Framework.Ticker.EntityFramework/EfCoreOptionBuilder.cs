using Framework.Ticker.EntityFrameworkCore.Customizer;
using Framework.Ticker.EntityFrameworkCore.DbContextFactory;
using Framework.Ticker.Utilities.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Framework.Ticker.EntityFrameworkCore;

public class TickerQEfCoreOptionBuilder<TTimeTicker, TCronTicker>
    where TTimeTicker : TimeTickerEntity<TTimeTicker>, new()
    where TCronTicker : CronTickerEntity, new()
{
    internal Action<IServiceCollection> ConfigureServices { get; set; } = _ => { };
    internal int PoolSize { get; set; } = 1024;
    internal string Schema { get; set; } = "ticker";

    public TickerQEfCoreOptionBuilder<TTimeTicker, TCronTicker> UseApplicationDbContext<TDbContext>(
        ConfigurationType configurationType
    )
        where TDbContext : DbContext
    {
        ServiceBuilder.UseApplicationDbContext<TDbContext, TTimeTicker, TCronTicker>(this, configurationType);
        return this;
    }

    public TickerQEfCoreOptionBuilder<TTimeTicker, TCronTicker> UseTickerQDbContext<TDbContext>(
        Action<DbContextOptionsBuilder> optionsAction,
        string? schema = null
    )
        where TDbContext : TickerQDbContext<TTimeTicker, TCronTicker>
    {
        Schema = schema ?? Schema;

        ServiceBuilder.UseTickerQDbContext<TDbContext, TTimeTicker, TCronTicker>(this, optionsAction);
        return this;
    }

    public TickerQEfCoreOptionBuilder<TTimeTicker, TCronTicker> SetDbContextPoolSize(int poolSize)
    {
        PoolSize = poolSize;
        return this;
    }

    public TickerQEfCoreOptionBuilder<TTimeTicker, TCronTicker> SetSchema(string schema)
    {
        Schema = schema;
        return this;
    }
}
