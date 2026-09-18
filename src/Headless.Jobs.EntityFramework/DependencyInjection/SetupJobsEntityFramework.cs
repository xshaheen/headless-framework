// Copyright (c) Mahmoud Shaheen. All rights reserved.

using FluentValidation;
using Headless.Checks;
using Headless.Jobs.Entities;
using Microsoft.Extensions.DependencyInjection;

#pragma warning disable IDE0130 // ReSharper disable once CheckNamespace
namespace Headless.Jobs;

public static class SetupJobsEntityFramework
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
    /// <paramref name="efConfiguration"/> to select a DbContext and configure pool size; the schema
    /// comes from <c>JobsOptionsBuilder.ConfigureStorage</c> on <paramref name="jobsConfiguration"/>.
    /// </remarks>
    /// <param name="jobsConfiguration">The jobs options builder.</param>
    /// <param name="efConfiguration">
    /// Optional callback to configure EF Core options. When <see langword="null"/>, defaults are used
    /// (pool size 1024).
    /// </param>
    /// <exception cref="ArgumentOutOfRangeException">The configured pool size is ≤ 0.</exception>
    public static JobsOptionsBuilder<TTimeJob, TCronJob> UseEntityFramework<TTimeJob, TCronJob>(
        this JobsOptionsBuilder<TTimeJob, TCronJob> jobsConfiguration,
        Action<JobsEfCoreOptionBuilder<TTimeJob, TCronJob>>? efConfiguration = null
    )
        where TTimeJob : TimeJobEntity<TTimeJob>, new()
        where TCronJob : CronJobEntity, new()
    {
        var efCoreOptionBuilder = new JobsEfCoreOptionBuilder<TTimeJob, TCronJob>();

        efConfiguration?.Invoke(efCoreOptionBuilder);

        Argument.IsPositive(efCoreOptionBuilder.PoolSize);

        // Opt into coordinated membership: the core pipeline requires a coordination provider and wires the
        // node@incarnation owner adapter + dead-node recovery bridge + registration startup gate. The old
        // ApplicationStarted self-reclaim hook is gone — recovery now flows through NodeLeft.
        jobsConfiguration.RequiresCoordinatedMembership = true;

        jobsConfiguration.ExternalProviderConfigServiceAction += (services) =>
            services.AddSingleton(_ => efCoreOptionBuilder);

        jobsConfiguration.ExternalProviderConfigServiceAction += services =>
        {
            // The schema is authored on the feature builder before the host exists, so the registered instance is
            // materialized from that snapshot. Every model path then reads the one resolved JobsStorageOptions
            // instead of a per-path default, which is what let the customizer and the reservation table drift.
            var storageOptions = services.AddOptions<JobsStorageOptions, JobsEntityFrameworkStorageOptionsValidator>();

            // Applied only when the callback overload authored something: an unconditional snapshot would write this
            // builder's untouched defaults over a schema the Core layer already bound from configuration.
            if (jobsConfiguration.HasStorageOptionsOverride)
            {
                storageOptions.Configure(options => jobsConfiguration.StorageOptions.CopyTo(options));
            }

            // Model building resolves the value type directly: a DbContext reaches application services through
            // its own GetService, and the schema is needed while the model is built, long before any IOptions
            // consumer would normally run.
            services.AddSingletonOptionValue<JobsStorageOptions>();
        };

        jobsConfiguration.ExternalProviderConfigServiceAction += efCoreOptionBuilder.ConfigureServices;

        return jobsConfiguration;
    }

    // EF dispatches to whatever database the consumer wired up, so this validator uses the cross-provider
    // identifier rule (the SQL Server superset) rather than a dialect-specific one; the database itself
    // surfaces any remaining length or character objection at schema-creation time.
    private sealed class JobsEntityFrameworkStorageOptionsValidator : AbstractValidator<JobsStorageOptions>
    {
        public JobsEntityFrameworkStorageOptionsValidator()
        {
            RuleFor(x => x.Schema).IsValidCrossProviderIdentifier();
        }
    }
}
