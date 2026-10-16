// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Globalization;
using Headless.DistributedLocks;
using Headless.Jobs.Internal;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Headless.Jobs.BackgroundServices;

/// <summary>
/// Handles Jobs core initialization (function building, notification wiring, external provider init, the
/// fingerprint drain). Seeding is <see cref="JobsSeeder"/>'s, which runs only when the application opts in.
/// Signals <see cref="JobsActivationBarrier"/> when it finishes, which is what actually orders the scheduler behind
/// it — registration order alone does not survive <c>HostOptions.ServicesStartConcurrently</c>.
/// </summary>
internal sealed class JobsInitializationHostedService(
    IServiceProvider serviceProvider,
    JobsActivationBarrier activationBarrier,
    ILogger<JobsInitializationHostedService> logger
) : IHostedService
{
    private Action<object?, CoreNotifyActionType>? _notifyCoreHandler;
    private JobsExecutionContext? _executionContext;

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        try
        {
            await _StartCoreAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            // Fail closed. Publish the failure to every loop parked on the barrier — including loops already running
            // under a concurrent start — so none of them proceeds to select work under an unverified schedule
            // interpretation, then let the original exception abort host startup as before.
            activationBarrier.MarkFailed(exception);

            throw;
        }

        activationBarrier.MarkCompleted();
    }

    private async Task _StartCoreAsync(CancellationToken cancellationToken)
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
        // were registered at all, gating both the notification wiring below and the fingerprint drain further down.
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

        // Drain one stable store snapshot here, before the caller opens the activation barrier and therefore before any
        // loop can pick up a uninitialized or stale-fingerprint row. The BARRIER is the ordering guarantee, not hosted-
        // service registration order: a host that sets HostOptions.ServicesStartConcurrently starts the scheduler at
        // the same time as this initializer. Deterministically invalid definitions are durably deferred by the manager;
        // storage/infrastructure failures propagate, leave the barrier closed-with-failure, and fail closed instead of
        // allowing dispatch under an unverified interpretation.
        if (backgroundScheduler is not null)
        {
            await DrainFingerprintSnapshotAsync(schedulerOptions, cancellationToken).ConfigureAwait(false);
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

    internal async Task DrainFingerprintSnapshotAsync(
        SchedulerOptionsBuilder schedulerOptions,
        CancellationToken cancellationToken
    )
    {
        var manager = serviceProvider.GetRequiredService<IInternalJobManager>();
        Guid? cursor = null;
        Guid? highWatermark = null;
        var scanned = 0;
        var rebased = 0;
        var deferred = 0;
        var lostFence = 0;
        CronFingerprintSweepResult result;
        while (true)
        {
            result = await manager
                .RebaseStaleFingerprintsAsync(
                    schedulerOptions.FingerprintSweepBatchSize,
                    afterId: cursor,
                    throughId: highWatermark,
                    cancellationToken: cancellationToken
                )
                .ConfigureAwait(false);
            scanned += result.Scanned;
            rebased += result.Rebased;
            deferred += result.Deferred;
            lostFence += result.LostFence;
            cursor = result.NextCursorId;
            highWatermark ??= result.SnapshotHighWatermarkId;

            if (result.HasMore)
            {
                if (cursor is null)
                {
                    throw new InvalidOperationException(
                        "Fingerprint sweep reported more rows without a continuation cursor."
                    );
                }

                continue;
            }

            break;
        }

        logger.CronFingerprintActivationCompleted(scanned, rebased, deferred, lostFence);
    }
}

internal static partial class JobsInitializationLog
{
    [LoggerMessage(
        EventId = 5,
        EventName = "CronFingerprintActivationCompleted",
        Level = LogLevel.Information,
        Message = "Cron fingerprint activation gate completed: scanned={Scanned}, rebased={Rebased}, "
            + "deferred={Deferred}, lostFence={LostFence}."
    )]
    public static partial void CronFingerprintActivationCompleted(
        this ILogger logger,
        int scanned,
        int rebased,
        int deferred,
        int lostFence
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
    public static partial void LeaseDurationShorterThanFallback(
        this ILogger logger,
        TimeSpan leaseDuration,
        TimeSpan fallbackInterval
    );

    [LoggerMessage(
        EventId = 2,
        EventName = "CronSeedMigrationSkipped",
        Level = LogLevel.Debug,
        Message = "Skipped cron-seed migration: another node holds the '"
            + JobsKeys.CronSeedMigrationResource
            + "' lock and is seeding."
    )]
    public static partial void CronSeedMigrationSkipped(this ILogger logger);

    [LoggerMessage(
        EventId = 3,
        EventName = "CronSeedMigrationLockAcquireFailed",
        // Warning, not Debug: a contention skip (lease == null) is normal on every rolling deploy, but an acquire
        // *fault* signals a lock-store problem. If the store is down for all nodes at first boot, every node hits
        // this path and the seed is skipped until the next restart — that must be operator-visible.
        Level = LogLevel.Warning,
        Message = "Skipped cron-seed migration: acquiring the '"
            + JobsKeys.CronSeedMigrationResource
            + "' lock failed. Another node will seed or the next boot will retry."
    )]
    public static partial void CronSeedMigrationLockAcquireFailed(this ILogger logger, Exception exception);
}
