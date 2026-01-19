using Framework.Ticker.BackgroundServices;
using Framework.Ticker.Dispatcher;
using Framework.Ticker.Provider;
using Framework.Ticker.TickerQThreadPool;
using Framework.Ticker.Utilities;
using Framework.Ticker.Utilities.Entities;
using Framework.Ticker.Utilities.Enums;
using Framework.Ticker.Utilities.Instrumentation;
using Framework.Ticker.Utilities.Interfaces;
using Framework.Ticker.Utilities.Interfaces.Managers;
using Framework.Ticker.Utilities.Managers;
using Framework.Ticker.Utilities.Temps;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using TickerQ;

namespace Framework.Ticker.DependencyInjection;

public static class TickerQServiceExtensions
{
    public static IServiceCollection AddTickerQ(
        this IServiceCollection services,
        Action<TickerOptionsBuilder<TimeTickerEntity, CronTickerEntity>>? optionsBuilder = null
    ) => services.AddTickerQ<TimeTickerEntity, CronTickerEntity>(optionsBuilder);

    public static IServiceCollection AddTickerQ<TTimeTicker, TCronTicker>(
        this IServiceCollection services,
        Action<TickerOptionsBuilder<TTimeTicker, TCronTicker>>? optionsBuilder = null
    )
        where TTimeTicker : TimeTickerEntity<TTimeTicker>, new()
        where TCronTicker : CronTickerEntity, new()
    {
        var tickerExecutionContext = new TickerExecutionContext();
        var schedulerOptionsBuilder = new SchedulerOptionsBuilder();
        var optionInstance = new TickerOptionsBuilder<TTimeTicker, TCronTicker>(
            tickerExecutionContext,
            schedulerOptionsBuilder
        );
        optionsBuilder?.Invoke(optionInstance);
        CronScheduleCache.TimeZoneInfo = schedulerOptionsBuilder.SchedulerTimeZone;

        // Apply JSON serializer options for ticker requests if configured during service registration
        if (optionInstance.RequestJsonSerializerOptions != null)
        {
            TickerHelper.RequestJsonSerializerOptions = optionInstance.RequestJsonSerializerOptions;
        }

        // Configure whether ticker request payloads should use GZip compression
        TickerHelper.UseGZipCompression = optionInstance.RequestGZipCompressionEnabled;
        services.AddSingleton<ITimeTickerManager<TTimeTicker>, TickerManager<TTimeTicker, TCronTicker>>();
        services.AddSingleton<ICronTickerManager<TCronTicker>, TickerManager<TTimeTicker, TCronTicker>>();
        services.AddSingleton<IInternalTickerManager, InternalTickerManager<TTimeTicker, TCronTicker>>();
        services.AddSingleton<ITickerQRedisContext, NoOpTickerQRedisContext>();
        services.AddSingleton<
            ITickerPersistenceProvider<TTimeTicker, TCronTicker>,
            TickerInMemoryPersistenceProvider<TTimeTicker, TCronTicker>
        >();
        services.AddSingleton<ITickerQNotificationHubSender, NoOpTickerQNotificationHubSender>();
        services.AddSingleton<ITickerClock, TickerSystemClock>();

        // Only register background services if enabled (default is true)
        if (optionInstance.RegisterBackgroundServices)
        {
            services.AddSingleton<TickerQSchedulerBackgroundService>();
            services.AddSingleton<ITickerQHostScheduler>(provider =>
                provider.GetRequiredService<TickerQSchedulerBackgroundService>()
            );
            services.AddHostedService(provider => provider.GetRequiredService<TickerQSchedulerBackgroundService>());
            services.AddHostedService(provider => provider.GetRequiredService<TickerQFallbackBackgroundService>());
            services.AddSingleton<TickerQFallbackBackgroundService>();
            services.AddSingleton<TickerExecutionTaskHandler>();
            services.AddSingleton<ITickerQDispatcher, TickerQDispatcher>();
            services.AddSingleton(sp =>
            {
                var notification = sp.GetRequiredService<ITickerQNotificationHubSender>();
                var notifyDebounce = new SoftSchedulerNotifyDebounce(
                    (value) => notification.UpdateActiveThreads(value)
                );
                return new TickerQTaskScheduler(
                    schedulerOptionsBuilder.MaxConcurrency,
                    schedulerOptionsBuilder.IdleWorkerTimeOut,
                    notifyDebounce
                );
            });
        }
        else
        {
            // Register NoOp implementations when background services are disabled
            services.AddSingleton<ITickerQHostScheduler, NoOpTickerQHostScheduler>();
            services.AddSingleton<ITickerQDispatcher, NoOpTickerQDispatcher>();
        }

        services.AddSingleton<ITickerQInstrumentation, LoggerInstrumentation>();

        optionInstance.ExternalProviderConfigServiceAction?.Invoke(services);
        optionInstance.DashboardServiceAction?.Invoke(services);

        if (optionInstance.TickerExceptionHandlerType != null)
        {
            services.AddSingleton(typeof(ITickerExceptionHandler), optionInstance.TickerExceptionHandlerType);
        }

        services.AddSingleton(_ => optionInstance);
        services.AddSingleton(_ => tickerExecutionContext);
        services.AddSingleton(_ => schedulerOptionsBuilder);
        return services;
    }

    /// <summary>
    /// Initializes TickerQ for generic host applications (Console, MAUI, WPF, Worker Services, etc.)
    /// </summary>
    public static IHost UseTickerQ(this IHost host, TickerQStartMode qStartMode = TickerQStartMode.Immediate)
    {
        _InitializeTickerQ(host.Services, qStartMode);
        return host;
    }

    /// <summary>
    /// Initializes TickerQ with a service provider directly
    /// </summary>
    public static IServiceProvider UseTickerQ(
        this IServiceProvider serviceProvider,
        TickerQStartMode qStartMode = TickerQStartMode.Immediate
    )
    {
        _InitializeTickerQ(serviceProvider, qStartMode);
        return serviceProvider;
    }

    private static void _InitializeTickerQ(IServiceProvider serviceProvider, TickerQStartMode qStartMode)
    {
        var tickerExecutionContext = serviceProvider.GetRequiredService<TickerExecutionContext>();
        var configuration = serviceProvider.GetRequiredService<IConfiguration>();
        var notificationHubSender = serviceProvider.GetRequiredService<ITickerQNotificationHubSender>();
        var backgroundScheduler = serviceProvider.GetRequiredService<TickerQSchedulerBackgroundService>();

        // If background services are registered, configure them
        if (backgroundScheduler != null)
        {
            backgroundScheduler.SkipFirstRun = qStartMode == TickerQStartMode.Manual;

            tickerExecutionContext.NotifyCoreAction += (value, type) =>
            {
                if (value == null)
                {
                    return;
                }

                if (type == CoreNotifyActionType.NotifyHostExceptionMessage)
                {
                    notificationHubSender.UpdateHostException(value);
                    tickerExecutionContext.LastHostExceptionMessage = (string)value;
                }
                else if (type == CoreNotifyActionType.NotifyNextOccurence)
                {
                    notificationHubSender.UpdateNextOccurrence(value);
                }
                else if (type == CoreNotifyActionType.NotifyHostStatus)
                {
                    notificationHubSender.UpdateHostStatus(value);
                }
                else if (type == CoreNotifyActionType.NotifyThreadCount)
                {
                    notificationHubSender.UpdateActiveThreads(value);
                }
            };
        }
        // If background services are not registered (due to DisableBackgroundServices()),
        // silently skip background service configuration. This is expected behavior.

        TickerFunctionProvider.UpdateCronExpressionsFromIConfiguration(configuration);
        TickerFunctionProvider.Build();

        // Run core seeding pipeline based on main options (works for both in-memory and EF providers).
        var options = tickerExecutionContext.OptionsSeeding;

        if (options == null || options.SeedDefinedCronTickers)
        {
            _SeedDefinedCronTickers(serviceProvider).GetAwaiter().GetResult();
        }

        if (options?.TimeSeederAction != null)
        {
            options.TimeSeederAction(serviceProvider).GetAwaiter().GetResult();
        }

        if (options?.CronSeederAction != null)
        {
            options.CronSeederAction(serviceProvider).GetAwaiter().GetResult();
        }

        // Let external providers (e.g., EF Core) perform their own startup logic (dead-node cleanup, etc.).
        if (tickerExecutionContext.ExternalProviderApplicationAction != null)
        {
            tickerExecutionContext.ExternalProviderApplicationAction(serviceProvider);
            tickerExecutionContext.ExternalProviderApplicationAction = null;
        }

        // Dashboard integration is handled by Framework.Ticker.Dashboard package via DashboardApplicationAction
        // It will be invoked when UseTickerQ is called from ASP.NET Core specific extension
    }

    private static async Task _SeedDefinedCronTickers(IServiceProvider serviceProvider)
    {
        var internalTickerManager = serviceProvider.GetRequiredService<IInternalTickerManager>();

        var functionsToSeed = TickerFunctionProvider
            .TickerFunctions.Where(x => !string.IsNullOrEmpty(x.Value.cronExpression))
            .Select(x => (x.Key, x.Value.cronExpression))
            .ToArray();

        await internalTickerManager.MigrateDefinedCronTickers(functionsToSeed);
    }
}
