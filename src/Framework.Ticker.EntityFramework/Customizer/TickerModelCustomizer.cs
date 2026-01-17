using Framework.Ticker.EntityFrameworkCore.Configurations;
using Framework.Ticker.Utilities.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;

namespace Framework.Ticker.EntityFrameworkCore.Customizer;

internal class TickerModelCustomizer<TTimeTicker, TCronTicker> : RelationalModelCustomizer
    where TTimeTicker : TimeTickerEntity<TTimeTicker>, new()
    where TCronTicker : CronTickerEntity, new()
{
    public TickerModelCustomizer(ModelCustomizerDependencies dependencies)
        : base(dependencies) { }

    public override void Customize(ModelBuilder builder, DbContext context)
    {
        builder.ApplyConfiguration(new TimeTickerConfigurations<TTimeTicker>());
        builder.ApplyConfiguration(new CronTickerConfigurations<TCronTicker>());
        builder.ApplyConfiguration(new CronTickerOccurrenceConfigurations<TCronTicker>());

        base.Customize(builder, context);
    }
}
