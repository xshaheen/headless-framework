using Framework.Ticker.Utilities;
using Framework.Ticker.Utilities.Entities;
using Framework.Ticker.Utilities.Interfaces;
using Framework.Ticker.Utilities.Interfaces.Managers;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Framework.Ticker.DependencyInjection;

public static class ServiceExtension
{
    public static TickerOptionsBuilder<TTimeTicker, TCronTicker> AddOperationalStore<TTimeTicker, TCronTicker>(
        this TickerOptionsBuilder<TTimeTicker, TCronTicker> tickerConfiguration,
        Action<TickerQEfCoreOptionBuilder<TTimeTicker, TCronTicker>>? efConfiguration = null
    )
        where TTimeTicker : TimeTickerEntity<TTimeTicker>, new()
        where TCronTicker : CronTickerEntity, new()
    {
        var efCoreOptionBuilder = new TickerQEfCoreOptionBuilder<TTimeTicker, TCronTicker>();

        efConfiguration?.Invoke(efCoreOptionBuilder);

        if (efCoreOptionBuilder.PoolSize <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(efCoreOptionBuilder.PoolSize),
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
        TickerOptionsBuilder<TTimeTicker, TCronTicker> tickerConfiguration,
        TickerQEfCoreOptionBuilder<TTimeTicker, TCronTicker> options
    )
        where TTimeTicker : TimeTickerEntity<TTimeTicker>, new()
        where TCronTicker : CronTickerEntity, new()
    {
        tickerConfiguration.UseExternalProviderApplication(
            (serviceProvider) =>
            {
                var internalTickerManager = serviceProvider.GetRequiredService<IInternalTickerManager>();
                var hostLifetime = serviceProvider.GetRequiredService<IHostApplicationLifetime>();
                var schedulerOptions = serviceProvider.GetRequiredService<SchedulerOptionsBuilder>();
                var hostScheduler = serviceProvider.GetService<ITickerQHostScheduler>();

                hostLifetime.ApplicationStarted.Register(() =>
                {
                    _ = Task.Run(async () =>
                    {
                        // Release resources held by dead nodes before the scheduler starts processing.
                        await internalTickerManager.ReleaseDeadNodeResources(schedulerOptions.NodeIdentifier);

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
