// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Jobs.Enums;
using Headless.Jobs.Interfaces;
using Headless.Jobs.Interfaces.Managers;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Headless.Jobs.BackgroundServices;

/// <summary>
/// Handles Jobs core initialization (function building, seeding, notification wiring, external provider init).
/// Registered before the scheduler to guarantee correct startup order.
/// </summary>
internal sealed class JobsInitializationHostedService(IServiceProvider serviceProvider) : IHostedService
{
    private Action<object?, CoreNotifyActionType>? _notifyCoreHandler;
    private JobsExecutionContext? _executionContext;

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        var executionContext = serviceProvider.GetRequiredService<JobsExecutionContext>();
        var configuration = serviceProvider.GetRequiredService<IConfiguration>();
        var notificationHubSender = serviceProvider.GetRequiredService<IJobsNotificationHubSender>();
        var schedulerOptions = serviceProvider.GetRequiredService<SchedulerOptionsBuilder>();

        // A pickup lease (LockedUntil = now + LeaseDuration) shorter than the fallback re-queue cadence can expire
        // between fallback ticks, letting another node speculatively re-claim a still-owned Idle/Queued row. The CAS
        // on the InProgress transition prevents double execution, but the redundant pickup is wasteful. Warn so the
        // operator widens LeaseDuration — the Jobs analog of messaging's DeadThreshold >= DispatchTimeout guard.
        if (schedulerOptions.LeaseDuration < schedulerOptions.FallbackIntervalChecker)
        {
            var logger = serviceProvider.GetRequiredService<ILogger<JobsInitializationHostedService>>();
            logger.LeaseDurationShorterThanFallback(
                schedulerOptions.LeaseDuration,
                schedulerOptions.FallbackIntervalChecker
            );
        }

        // #316 ValidateOnStart-equivalent: reject a misconfigured explicit lease-renewal cadence at startup
        // (must be positive and strictly less than LeaseDuration) so a bad value fails fast rather than silently
        // letting a running job's lease lapse. Throws InvalidOperationException; no-op for the derived default.
        schedulerOptions.ResolveLeaseRenewalInterval();

        // Configure scheduler start mode
        var backgroundScheduler = serviceProvider.GetService<JobsSchedulerBackgroundService>();
        if (backgroundScheduler is not null)
        {
            backgroundScheduler.SkipFirstRun = schedulerOptions.StartMode == JobsStartMode.Manual;

            _executionContext = executionContext;
            _notifyCoreHandler = (value, type) =>
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
            executionContext.NotifyCoreAction += _notifyCoreHandler;
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

    public Task StopAsync(CancellationToken cancellationToken)
    {
        if (_executionContext is not null && _notifyCoreHandler is not null)
        {
            _executionContext.NotifyCoreAction -= _notifyCoreHandler;
        }

        return Task.CompletedTask;
    }

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

internal static partial class JobsInitializationLog
{
    [LoggerMessage(
        EventId = 1,
        EventName = "LeaseDurationShorterThanFallback",
        Level = LogLevel.Warning,
        Message = "SchedulerOptionsBuilder.LeaseDuration ({LeaseDuration}) is shorter than FallbackIntervalChecker "
            + "({FallbackInterval}). A pickup lease can expire before the fallback re-queues the row, letting another "
            + "node speculatively re-claim a still-owned Idle/Queued job. Set LeaseDuration >= FallbackIntervalChecker "
            + "to avoid redundant pickups."
    )]
    public static partial void LeaseDurationShorterThanFallback(
        this ILogger logger,
        TimeSpan leaseDuration,
        TimeSpan fallbackInterval
    );
}
