// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Jobs.Entities;
using Microsoft.Extensions.DependencyInjection;

namespace Headless.Jobs.DependencyInjection;

public static class ServiceExtension
{
    public static JobsOptionsBuilder<TTimeJob, TCronJob> AddOperationalStore<TTimeJob, TCronJob>(
        this JobsOptionsBuilder<TTimeJob, TCronJob> jobsConfiguration,
        Action<JobsEfCoreOptionBuilder<TTimeJob, TCronJob>>? efConfiguration = null
    )
        where TTimeJob : TimeJobEntity<TTimeJob>, new()
        where TCronJob : CronJobEntity, new()
    {
        var efCoreOptionBuilder = new JobsEfCoreOptionBuilder<TTimeJob, TCronJob>();

        efConfiguration?.Invoke(efCoreOptionBuilder);

        if (efCoreOptionBuilder.PoolSize <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(efConfiguration),
                efCoreOptionBuilder.PoolSize,
                "Pool size must be greater than 0"
            );
        }

        // Opt into coordinated membership: the core pipeline requires a coordination provider and wires the
        // node@incarnation owner adapter + dead-node recovery bridge + registration startup gate. The old
        // ApplicationStarted self-reclaim hook is gone (KTD3) — recovery now flows through NodeLeft.
        jobsConfiguration.RequiresCoordinatedMembership = true;

        jobsConfiguration.ExternalProviderConfigServiceAction += (services) =>
            services.AddSingleton(_ => efCoreOptionBuilder);

        jobsConfiguration.ExternalProviderConfigServiceAction += efCoreOptionBuilder.ConfigureServices;

        return jobsConfiguration;
    }
}
