using Headless.Jobs.BackgroundServices;
using Headless.Jobs.Dispatcher;
using Headless.Jobs.Entities;
using Headless.Jobs.Enums;
using Headless.Jobs.Instrumentation;
using Headless.Jobs.Interfaces;
using Headless.Jobs.Interfaces.Managers;
using Headless.Jobs.Managers;
using Headless.Jobs.Provider;
using Headless.Jobs.Temps;
using Headless.Jobs.JobsThreadPool;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Headless.Jobs.DependencyInjection;

public static class JobsServiceExtensions
{
    public static IServiceCollection AddJobs(
        this IServiceCollection services,
        Action<TickerOptionsBuilder<TimeTickerEntity, CronTickerEntity>>? optionsBuilder = null
    ) => services.AddJobs<TimeTickerEntity, CronTickerEntity>(optionsBuilder);

    public static IServiceCollection AddJobs<TTimeTicker, TCronTicker>(
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
        services.AddSingleton<IJobsRedisContext, NoOpJobsRedisContext>();
        services.AddSingleton<
            ITickerPersistenceProvider<TTimeTicker, TCronTicker>,
            TickerInMemoryPersistenceProvider<TTimeTicker, TCronTicker>
        >();
        services.AddSingleton<IJobsNotificationHubSender, NoOpJobsNotificationHubSender>();
        services.AddSingleton<ITickerClock, TickerSystemClock>();

        // Only register background services if enabled (default is true)
        if (optionInstance.RegisterBackgroundServices)
        {
            services.AddSingleton<JobsSchedulerBackgroundService>();
            services.AddSingleton<IJobsHostScheduler>(provider =>
                provider.GetRequiredService<JobsSchedulerBackgroundService>()
            );
            services.AddHostedService(provider => provider.GetRequiredService<JobsSchedulerBackgroundService>());
            services.AddHostedService(provider => provider.GetRequiredService<JobsFallbackBackgroundService>());
            services.AddSingleton<JobsFallbackBackgroundService>();
            services.AddSingleton<TickerExecutionTaskHandler>();
            services.AddSingleton<IJobsDispatcher, JobsDispatcher>();
            services.AddSingleton(sp =>
            {
                var notification = sp.GetRequiredService<IJobsNotificationHubSender>();
                var notifyDebounce = new SoftSchedulerNotifyDebounce(
                    (value) => notification.UpdateActiveThreads(value)
                );
                return new JobsTaskScheduler(
                    schedulerOptionsBuilder.MaxConcurrency,
                    schedulerOptionsBuilder.IdleWorkerTimeOut,
                    notifyDebounce
                );
            });
        }
        else
        {
            // Register NoOp implementations when background services are disabled
            services.AddSingleton<IJobsHostScheduler, NoOpJobsHostScheduler>();
            services.AddSingleton<IJobsDispatcher, NoOpJobsDispatcher>();
        }

        services.AddSingleton<ITickerFunctionConcurrencyGate, TickerFunctionConcurrencyGate>();
        services.AddSingleton<IJobsInstrumentation, LoggerInstrumentation>();

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
    /// Initializes Jobs for generic host applications (Console, MAUI, WPF, Worker Services, etc.)
    /// </summary>
    public static IHost UseJobs(this IHost host, JobsStartMode qStartMode = JobsStartMode.Immediate)
    {
        _InitializeJobs(host.Services, qStartMode);
        return host;
    }

    /// <summary>
    /// Initializes Jobs with a service provider directly
    /// </summary>
    public static IServiceProvider UseJobs(
        this IServiceProvider serviceProvider,
        JobsStartMode qStartMode = JobsStartMode.Immediate
    )
    {
        _InitializeJobs(serviceProvider, qStartMode);
        return serviceProvider;
    }

    private static void _InitializeJobs(IServiceProvider serviceProvider, JobsStartMode qStartMode)
    {
        var tickerExecutionContext = serviceProvider.GetRequiredService<TickerExecutionContext>();
        var configuration = serviceProvider.GetRequiredService<IConfiguration>();
        var notificationHubSender = serviceProvider.GetRequiredService<IJobsNotificationHubSender>();
        var backgroundScheduler = serviceProvider.GetRequiredService<JobsSchedulerBackgroundService>();

        // If background services are registered, configure them
        if (backgroundScheduler != null)
        {
            backgroundScheduler.SkipFirstRun = qStartMode == JobsStartMode.Manual;

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

        // Dashboard integration is handled by Headless.Jobs.Dashboard package via DashboardApplicationAction
        // It will be invoked when UseJobs is called from ASP.NET Core specific extension
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
