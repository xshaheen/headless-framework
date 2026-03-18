using Headless.Jobs.BackgroundServices;
using Headless.Jobs.Dispatcher;
using Headless.Jobs.Entities;
using Headless.Jobs.Instrumentation;
using Headless.Jobs.Interfaces;
using Headless.Jobs.Interfaces.Managers;
using Headless.Jobs.JobsThreadPool;
using Headless.Jobs.Managers;
using Headless.Jobs.Provider;
using Headless.Jobs.Temps;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Headless.Jobs.DependencyInjection;

public static class JobsServiceExtensions
{
    public static IServiceCollection AddHeadlessJobs(
        this IServiceCollection services,
        Action<JobsOptionsBuilder<TimeJobEntity, CronJobEntity>>? optionsBuilder = null
    ) => services.AddHeadlessJobs<TimeJobEntity, CronJobEntity>(optionsBuilder);

    public static IServiceCollection AddHeadlessJobs<TTimeJob, TCronJob>(
        this IServiceCollection services,
        Action<JobsOptionsBuilder<TTimeJob, TCronJob>>? optionsBuilder = null
    )
        where TTimeJob : TimeJobEntity<TTimeJob>, new()
        where TCronJob : CronJobEntity, new()
    {
        var tickerExecutionContext = new JobsExecutionContext();
        var schedulerOptionsBuilder = new SchedulerOptionsBuilder();
        var optionInstance = new JobsOptionsBuilder<TTimeJob, TCronJob>(
            tickerExecutionContext,
            schedulerOptionsBuilder
        );
        optionsBuilder?.Invoke(optionInstance);
        CronScheduleCache.TimeZoneInfo = schedulerOptionsBuilder.SchedulerTimeZone;

        // Apply JSON serializer options for job requests if configured during service registration
        if (optionInstance.RequestJsonSerializerOptions != null)
        {
            JobsHelper.RequestJsonSerializerOptions = optionInstance.RequestJsonSerializerOptions;
        }

        // Configure whether job request payloads should use GZip compression
        JobsHelper.UseGZipCompression = optionInstance.RequestGZipCompressionEnabled;
        services.AddSingleton<ITimeJobManager<TTimeJob>, JobsManager<TTimeJob, TCronJob>>();
        services.AddSingleton<ICronJobManager<TCronJob>, JobsManager<TTimeJob, TCronJob>>();
        services.AddSingleton<IInternalJobManager, InternalJobsManager<TTimeJob, TCronJob>>();
        services.AddSingleton<IJobsRedisContext, NoOpJobsRedisContext>();
        services.AddSingleton<
            IJobPersistenceProvider<TTimeJob, TCronJob>,
            JobsInMemoryPersistenceProvider<TTimeJob, TCronJob>
        >();
        services.AddSingleton<IJobsNotificationHubSender, NoOpJobsNotificationHubSender>();
        services.TryAddSingleton(TimeProvider.System);

        // Core initialization — registered before background services to guarantee startup order
        services.AddHostedService<JobsInitializationHostedService>();

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
            services.AddSingleton<JobsExecutionTaskHandler>();
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

        services.AddSingleton<IJobFunctionConcurrencyGate, JobFunctionConcurrencyGate>();
        services.AddSingleton<IJobsInstrumentation, LoggerInstrumentation>();

        optionInstance.ExternalProviderConfigServiceAction?.Invoke(services);
        optionInstance.DashboardServiceAction?.Invoke(services);

        if (optionInstance.JobExceptionHandlerType != null)
        {
            services.AddSingleton(typeof(IJobExceptionHandler), optionInstance.JobExceptionHandlerType);
        }

        services.AddSingleton(_ => optionInstance);
        services.AddSingleton(_ => tickerExecutionContext);
        services.AddSingleton(_ => schedulerOptionsBuilder);
        return services;
    }
}
