using Headless.Jobs.Entities;
using Headless.Jobs.Interfaces;
using Headless.Jobs.Interfaces.Managers;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

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

        jobsConfiguration.ExternalProviderConfigServiceAction += (services) =>
            services.AddSingleton(_ => efCoreOptionBuilder);

        jobsConfiguration.ExternalProviderConfigServiceAction += efCoreOptionBuilder.ConfigureServices;

        _UseApplicationService(jobsConfiguration, efCoreOptionBuilder);

        return jobsConfiguration;
    }

    private static void _UseApplicationService<TTimeJob, TCronJob>(
        JobsOptionsBuilder<TTimeJob, TCronJob> jobsConfiguration,
        JobsEfCoreOptionBuilder<TTimeJob, TCronJob> options
    )
        where TTimeJob : TimeJobEntity<TTimeJob>, new()
        where TCronJob : CronJobEntity, new()
    {
        jobsConfiguration.UseExternalProviderApplication(
            (serviceProvider) =>
            {
                var internalJobsManager = serviceProvider.GetRequiredService<IInternalJobManager>();
                var hostLifetime = serviceProvider.GetRequiredService<IHostApplicationLifetime>();
                var schedulerOptions = serviceProvider.GetRequiredService<SchedulerOptionsBuilder>();
                var hostScheduler = serviceProvider.GetService<IJobsHostScheduler>();

                hostLifetime.ApplicationStarted.Register(() =>
                {
                    _ = Task.Run(async () =>
                    {
                        // Release resources held by dead nodes before the scheduler starts processing.
                        await internalJobsManager.ReleaseDeadNodeResources(schedulerOptions.NodeIdentifier);

                        // After cleanup, restart the host scheduler so it immediately
                        // picks up newly seeded cron jobs and jobs configured via the core pipeline.
                        if (hostScheduler is { IsRunning: true })
                        {
                            hostScheduler.Restart();
                        }
                    });
                });
            }
        );
    }
}
