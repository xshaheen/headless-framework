// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Globalization;
using Headless.DistributedLocks;
using Headless.Jobs.Internal;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Headless.Jobs.BackgroundServices;

/// <summary>
/// Handles the cheap part of Jobs core initialization (function building, option checks, notification wiring,
/// external provider init). Seeding is <see cref="JobsSeeder"/>'s, which runs only when the application opts in.
/// </summary>
/// <remarks>
/// The cron fingerprint drain that opens <see cref="JobsActivationBarrier"/> is <see cref="JobsActivationWarmup"/>'s:
/// it touches the store once per page and per stale row, so it runs after the host has started instead of holding the
/// start open. On a host without background services nothing selects work, so this service opens the barrier itself.
/// </remarks>
internal sealed class JobsInitializationHostedService(
    IServiceProvider serviceProvider,
    JobsActivationBarrier activationBarrier,
    ILogger<JobsInitializationHostedService> logger
) : IHostedService
{
    private Action<object?, CoreNotifyActionType>? _notifyCoreHandler;
    private JobsExecutionContext? _executionContext;

    public Task StartAsync(CancellationToken cancellationToken)
    {
        try
        {
            _StartCore();
        }
        catch (Exception exception)
        {
            // Fail closed. Publish the failure to every loop parked on the barrier — including loops already running
            // under a concurrent start — so none of them proceeds to select work on a host whose configuration was
            // rejected, then let the original exception abort host startup.
            activationBarrier.MarkFailed(exception);

            throw;
        }

        return Task.CompletedTask;
    }

    private void _StartCore()
    {
        _ = serviceProvider.GetRequiredService<JobSchedulingPolicies>();
        var executionContext = serviceProvider.GetRequiredService<JobsExecutionContext>();
        var notificationHubSender = serviceProvider.GetRequiredService<IJobsNotificationHubSender>();
        var schedulerOptions = serviceProvider.GetRequiredService<SchedulerOptionsBuilder>();

        // A pickup lease (LockedUntil = now + LeaseDuration) shorter than the fallback re-queue cadence can expire
        // between fallback ticks, letting another node speculatively re-claim a still-owned Idle/Queued row. The CAS
        // on the InProgress transition prevents double execution, but the redundant pickup is wasteful. Warn so the
        // operator widens LeaseDuration — the Jobs analog of messaging's DeadThreshold >= DispatchTimeout guard.
        if (schedulerOptions.LeaseDuration < schedulerOptions.FallbackIntervalChecker)
        {
            logger.LeaseDurationShorterThanFallback(
                schedulerOptions.LeaseDuration,
                schedulerOptions.FallbackIntervalChecker
            );
        }

        // #316 ValidateOnStart-equivalent: reject a misconfigured explicit lease-renewal cadence at startup
        // (must be positive and strictly less than LeaseDuration) so a bad value fails fast rather than silently
        // letting a running job's lease lapse. Throws InvalidOperationException; no-op for the derived default.
        schedulerOptions.ResolveLeaseRenewalInterval();

        // Probe for the background scheduler: its presence is what tells this initializer whether background services
        // were registered at all, gating both the notification wiring below and who opens the activation barrier.
        // JobsStartMode.Manual is deliberately NOT pushed onto it from here — the scheduler consumes its own configured
        // start mode, because a push only lands in time when this initializer starts first, which
        // HostOptions.ServicesStartConcurrently does not guarantee.
        var backgroundScheduler = serviceProvider.GetService<JobsSchedulerBackgroundService>();
        if (backgroundScheduler is not null)
        {
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

        // External provider init (e.g., EF Core dead-node cleanup)
        if (executionContext.ExternalProviderApplicationAction is not null)
        {
            executionContext.ExternalProviderApplicationAction(serviceProvider);
            executionContext.ExternalProviderApplicationAction = null;
        }

        // A host that runs no job loops has nothing to drain the fingerprints for. A host that does keeps the barrier
        // closed here: JobsActivationWarmup drains one stable snapshot after the start and only then opens it.
        if (backgroundScheduler is null)
        {
            activationBarrier.MarkCompleted();
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
}

internal static partial class JobsInitializationLog
{
    [LoggerMessage(
        EventId = 5,
        EventName = "CronFingerprintActivationCompleted",
        Level = LogLevel.Information,
        Message = "Cron fingerprint activation gate completed: scanned={Scanned}, rebased={Rebased}, "
            + "deferred={Deferred}, lostFence={LostFence}, passDeferredBy={Deferral}."
    )]
    internal static partial void CronFingerprintActivationCompleted(
        this ILogger logger,
        int scanned,
        int rebased,
        int deferred,
        int lostFence,
        CronFingerprintSweepDeferralReason? deferral
    );

    [LoggerMessage(
        EventId = 1,
        EventName = "LeaseDurationShorterThanFallback",
        Level = LogLevel.Warning,
        Message = "SchedulerOptionsBuilder.LeaseDuration ({LeaseDuration}) is shorter than FallbackIntervalChecker "
            + "({FallbackInterval}). A pickup lease can expire before the fallback re-queues the row, letting another "
            + "node speculatively re-claim a still-owned Idle/Queued job. Set LeaseDuration >= FallbackIntervalChecker "
            + "to avoid redundant pickups."
    )]
    internal static partial void LeaseDurationShorterThanFallback(
        this ILogger logger,
        TimeSpan leaseDuration,
        TimeSpan fallbackInterval
    );

    [LoggerMessage(
        EventId = 2,
        EventName = "CronSeedMigrationLockTimedOut",
        Level = LogLevel.Error,
        Message = "Cron-seed migration failed: the '"
            + JobsKeys.CronSeedMigrationResource
            + "' lock was still held after {Wait}, so the seed did not run."
    )]
    internal static partial void CronSeedMigrationLockTimedOut(this ILogger logger, TimeSpan wait);

    [LoggerMessage(
        EventId = 3,
        EventName = "CronSeedMigrationLockAcquireFailed",
        // Error: an acquire fault signals a lock-store problem and fails the seed.
        Level = LogLevel.Error,
        Message = "Cron-seed migration failed: acquiring the '"
            + JobsKeys.CronSeedMigrationResource
            + "' lock failed, so the seed fails. Fix the lock store, then run the seed again."
    )]
    internal static partial void CronSeedMigrationLockAcquireFailed(this ILogger logger, Exception exception);
}
