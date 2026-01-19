using Framework.Ticker.Configurations;
using Framework.Ticker.Utilities.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;

namespace Framework.Ticker.Customizer;

internal sealed class TickerModelCustomizer<TTimeTicker, TCronTicker>(ModelCustomizerDependencies dependencies)
    : RelationalModelCustomizer(dependencies)
    where TTimeTicker : TimeTickerEntity<TTimeTicker>, new()
    where TCronTicker : CronTickerEntity, new()
{
    public override void Customize(ModelBuilder builder, DbContext context)
    {
        builder.ApplyConfiguration(new TimeTickerConfigurations<TTimeTicker>());
        builder.ApplyConfiguration(new CronTickerConfigurations<TCronTicker>());
        builder.ApplyConfiguration(new CronTickerOccurrenceConfigurations<TCronTicker>());

        base.Customize(builder, context);
    }
}
