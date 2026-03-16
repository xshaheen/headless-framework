using Headless.Jobs.Enums;
using Headless.Jobs.Interfaces;
using Headless.Jobs.Interfaces.Managers;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Headless.Jobs.BackgroundServices;

/// <summary>
/// Handles Jobs core initialization (function building, seeding, notification wiring, external provider init).
/// Registered before the scheduler to guarantee correct startup order.
/// </summary>
internal sealed class JobsInitializationHostedService(IServiceProvider serviceProvider) : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        var executionContext = serviceProvider.GetRequiredService<JobsExecutionContext>();
        var configuration = serviceProvider.GetRequiredService<IConfiguration>();
        var notificationHubSender = serviceProvider.GetRequiredService<IJobsNotificationHubSender>();
        var schedulerOptions = serviceProvider.GetRequiredService<SchedulerOptionsBuilder>();

        // Configure scheduler start mode
        var backgroundScheduler = serviceProvider.GetService<JobsSchedulerBackgroundService>();
        if (backgroundScheduler is not null)
        {
            backgroundScheduler.SkipFirstRun = schedulerOptions.StartMode == JobsStartMode.Manual;

            executionContext.NotifyCoreAction += (value, type) =>
            {
                if (value is null)
                {
                    return;
                }

                switch (type)
                {
                    case CoreNotifyActionType.NotifyHostExceptionMessage:
                        notificationHubSender.UpdateHostException(value);
                        executionContext.LastHostExceptionMessage = (string)value;

                        break;
                    case CoreNotifyActionType.NotifyNextOccurence:
                        notificationHubSender.UpdateNextOccurrence(value);

                        break;
                    case CoreNotifyActionType.NotifyHostStatus:
                        notificationHubSender.UpdateHostStatus(value);

                        break;
                    case CoreNotifyActionType.NotifyThreadCount:
                        notificationHubSender.UpdateActiveThreads(value);

                        break;
                }
            };
        }

        // Build function metadata
        JobFunctionProvider.UpdateCronExpressionsFromIConfiguration(configuration);
        JobFunctionProvider.Build();

        // Seeding pipeline
        var options = executionContext.OptionsSeeding;

        if (options is null || options.SeedDefinedCronJobs)
        {
            await _SeedDefinedCronJobsAsync(serviceProvider).ConfigureAwait(false);
        }

        if (options?.TimeSeederAction is not null)
        {
            await options.TimeSeederAction(serviceProvider).ConfigureAwait(false);
        }

        if (options?.CronSeederAction is not null)
        {
            await options.CronSeederAction(serviceProvider).ConfigureAwait(false);
        }

        // External provider init (e.g., EF Core dead-node cleanup)
        if (executionContext.ExternalProviderApplicationAction is not null)
        {
            executionContext.ExternalProviderApplicationAction(serviceProvider);
            executionContext.ExternalProviderApplicationAction = null;
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    private static async Task _SeedDefinedCronJobsAsync(IServiceProvider serviceProvider)
    {
        var internalJobsManager = serviceProvider.GetRequiredService<IInternalJobManager>();

        var functionsToSeed = JobFunctionProvider
            .JobFunctions.Where(x => !string.IsNullOrEmpty(x.Value.cronExpression))
            .Select(x => (x.Key, x.Value.cronExpression))
            .ToArray();

        await internalJobsManager.MigrateDefinedCronJobs(functionsToSeed).ConfigureAwait(false);
    }
}
