using Headless.Jobs.Configurations;
using Headless.Jobs.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;

namespace Headless.Jobs.Customizer;

internal sealed class JobsModelCustomizer<TTimeJob, TCronJob>(ModelCustomizerDependencies dependencies)
    : RelationalModelCustomizer(dependencies)
    where TTimeJob : TimeJobEntity<TTimeJob>, new()
    where TCronJob : CronJobEntity, new()
{
    public override void Customize(ModelBuilder builder, DbContext context)
    {
        builder.ApplyConfiguration(new TimeJobConfigurations<TTimeJob>());
        builder.ApplyConfiguration(new CronJobConfigurations<TCronJob>());
        builder.ApplyConfiguration(new CronJobOccurrenceConfigurations<TCronJob>());

        base.Customize(builder, context);
    }
}
