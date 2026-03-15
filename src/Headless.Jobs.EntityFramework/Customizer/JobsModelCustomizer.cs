using Headless.Jobs.Configurations;
using Headless.Jobs.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;

namespace Headless.Jobs.Customizer;

internal sealed class JobsModelCustomizer<TTimeTicker, TCronTicker>(ModelCustomizerDependencies dependencies)
    : RelationalModelCustomizer(dependencies)
    where TTimeTicker : TimeJobEntity<TTimeTicker>, new()
    where TCronTicker : CronJobEntity, new()
{
    public override void Customize(ModelBuilder builder, DbContext context)
    {
        builder.ApplyConfiguration(new TimeJobConfigurations<TTimeTicker>());
        builder.ApplyConfiguration(new CronJobConfigurations<TCronTicker>());
        builder.ApplyConfiguration(new CronJobOccurrenceConfigurations<TCronTicker>());

        base.Customize(builder, context);
    }
}
