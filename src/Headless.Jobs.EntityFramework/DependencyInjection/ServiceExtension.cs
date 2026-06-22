// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Jobs.Entities;
using Microsoft.Extensions.DependencyInjection;

namespace Headless.Jobs.DependencyInjection;

public static class ServiceExtension
{
    /// <summary>
    /// Registers the Entity Framework Core operational store as the persistence backend for the Jobs
    /// scheduler. Opts the node into coordinated membership so that distributed lease management,
    /// dead-node recovery, and node-death policy enforcement are active.
    /// </summary>
    /// <remarks>
    /// Coordinated membership requires exactly one <c>INodeMembership</c> provider (e.g.,
    /// <c>Headless.Coordination.EntityFramework</c>) to be registered. Without it the application
    /// will fail to start. Use <see cref="JobsEfCoreOptionBuilder{TTimeJob,TCronJob}"/> via
    /// <paramref name="efConfiguration"/> to select a DbContext and configure pool size and schema.
    /// </remarks>
    /// <param name="jobsConfiguration">The jobs options builder.</param>
    /// <param name="efConfiguration">
    /// Optional callback to configure EF Core options. When <see langword="null"/>, defaults are used
    /// (pool size 1024, schema "jobs").
    /// </param>
    /// <exception cref="ArgumentOutOfRangeException">The configured pool size is ≤ 0.</exception>
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
