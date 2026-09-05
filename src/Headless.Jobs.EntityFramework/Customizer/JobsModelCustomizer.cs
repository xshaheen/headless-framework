// Copyright (c) Mahmoud Shaheen. All rights reserved.

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
        var contractCollation = context.Database.ProviderName switch
        {
            "Microsoft.EntityFrameworkCore.SqlServer" => "Latin1_General_100_BIN2",
            "Npgsql.EntityFrameworkCore.PostgreSQL" => "C",
            _ => (string?)null,
        };

        builder.ApplyConfiguration(new TimeJobConfigurations<TTimeJob>(contractCollation: contractCollation));
        builder.ApplyConfiguration(new CronJobConfigurations<TCronJob>(contractCollation: contractCollation));
        builder.ApplyConfiguration(new CronJobOccurrenceConfigurations<TCronJob>(contractCollation: contractCollation));

        base.Customize(builder, context);
    }
}
