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
        var contractCollation = JobsContractCollation.TryResolve(context.Database.ProviderName);
        // Read from the same option the dedicated JobsDbContext reads. This path previously took the mapping
        // defaults, so a consumer-hosted context put every Jobs table in "jobs" whatever the override said.
        var schema = context.GetService<JobsStorageOptions>().Schema;

        builder.ApplyConfiguration(new TimeJobConfigurations<TTimeJob>(schema, contractCollation));
        builder.ApplyConfiguration(new CronJobConfigurations<TCronJob>(schema, contractCollation));
        builder.ApplyConfiguration(new CronJobOccurrenceConfigurations<TCronJob>(schema, contractCollation));
        JobsIdempotencyModelConfiguration.Configure(builder, schema, contractCollation);

        base.Customize(builder, context);
        // Consumer OnModelCreating may rename any column. Build owned SQL only after those mappings have settled.
        JobsKeyedModelConfiguration.Configure<TTimeJob>(builder, context);
    }
}
