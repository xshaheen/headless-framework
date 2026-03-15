using Headless.Jobs.Entities;
using Headless.Jobs.Interfaces;
using Headless.Jobs.Interfaces.Managers;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Headless.Jobs.DependencyInjection;

public static class ServiceExtension
{
    public static JobsOptionsBuilder<TTimeTicker, TCronTicker> AddOperationalStore<TTimeTicker, TCronTicker>(
        this JobsOptionsBuilder<TTimeTicker, TCronTicker> tickerConfiguration,
        Action<JobsEfCoreOptionBuilder<TTimeTicker, TCronTicker>>? efConfiguration = null
    )
        where TTimeTicker : TimeJobEntity<TTimeTicker>, new()
        where TCronTicker : CronJobEntity, new()
    {
        var efCoreOptionBuilder = new JobsEfCoreOptionBuilder<TTimeTicker, TCronTicker>();

        efConfiguration?.Invoke(efCoreOptionBuilder);

        if (efCoreOptionBuilder.PoolSize <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(efConfiguration),
                efCoreOptionBuilder.PoolSize,
                "Pool size must be greater than 0"
            );
        }

        tickerConfiguration.ExternalProviderConfigServiceAction += (services) =>
            services.AddSingleton(_ => efCoreOptionBuilder);

        tickerConfiguration.ExternalProviderConfigServiceAction += efCoreOptionBuilder.ConfigureServices;

        _UseApplicationService(tickerConfiguration, efCoreOptionBuilder);

        return tickerConfiguration;
    }

    private static void _UseApplicationService<TTimeTicker, TCronTicker>(
        JobsOptionsBuilder<TTimeTicker, TCronTicker> tickerConfiguration,
        JobsEfCoreOptionBuilder<TTimeTicker, TCronTicker> options
    )
        where TTimeTicker : TimeJobEntity<TTimeTicker>, new()
        where TCronTicker : CronJobEntity, new()
    {
        tickerConfiguration.UseExternalProviderApplication(
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
                        // picks up newly seeded cron tickers and jobs configured via the core pipeline.
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
