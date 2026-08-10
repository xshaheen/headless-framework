// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Globalization;
using Headless.DistributedLocks;
using Headless.Jobs.Enums;
using Headless.Jobs.Exceptions;
using Headless.Jobs.Interfaces;
using Headless.Jobs.Interfaces.Managers;
using Headless.Jobs.Internal;
using Headless.Jobs.Models;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Headless.Jobs.BackgroundServices;

/// <summary>
/// Handles Jobs core initialization (function building, seeding, notification wiring, external provider init).
/// Registered before the scheduler to guarantee correct startup order.
/// </summary>
internal sealed class JobsInitializationHostedService(
    IServiceProvider serviceProvider,
    JobFunctionRegistry functionRegistry,
    ILogger<JobsInitializationHostedService> logger
) : IHostedService
{
    private Action<object?, CoreNotifyActionType>? _notifyCoreHandler;
    private JobsExecutionContext? _executionContext;

    public async Task StartAsync(CancellationToken cancellationToken)
    {
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

        // Seeding pipeline
        var options = executionContext.OptionsSeeding;

        if (options?.SeedDefinedCronJobs != false)
        {
            await SeedDefinedCronJobsAsync(schedulerOptions, cancellationToken).ConfigureAwait(false);
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

        // Hosted services start sequentially in registration order. Drain one stable store snapshot here, before this
        // initializer returns and therefore before the scheduler can pick up a legacy/null or stale-fingerprint row.
        // Deterministically invalid definitions are durably deferred by the manager; storage/infrastructure failures
        // propagate and fail closed instead of allowing dispatch under an unverified interpretation.
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

    // Instance method (not static) so the lock + logger come from constructor injection rather than a mid-body
    // service-locator — resolution happens at DI-build time, inside the construction fault boundary, consistent with
    // JobsDeadOwnerReclaimer. Internal (not private) is the codebase's standard InternalsVisibleTo test seam: the guard
    // unit test constructs the service and calls this directly, avoiding a full StartAsync run.
    internal async Task SeedDefinedCronJobsAsync(
        SchedulerOptionsBuilder schedulerOptions,
        CancellationToken cancellationToken
    )
    {
        var internalJobsManager = serviceProvider.GetRequiredService<IInternalJobManager>();

        // Resolve the recovery knobs HERE rather than in the provider: attribute value, else the scheduler-wide
        // setting, else the framework default. The threshold has to be identical on every node — if each provider
        // resolved it from local configuration, two nodes could disagree about whether the same instant misfired.
        var functionsToSeed = functionRegistry
            .Functions.Where(x => !string.IsNullOrEmpty(x.Value.CronExpression))
            .Select(x => new CronSeedDefinition(
                x.Key,
                x.Value.CronExpression,
                _ResolveMissedRunPolicy(x.Key, x.Value.OnMissedRun, schedulerOptions.DefaultMissedRunPolicy),
                _ResolveGraceSeconds(
                    x.Key,
                    x.Value.MissedRunGraceSeconds,
                    schedulerOptions.DefaultMissedRunGraceSeconds
                ),
                serviceProvider.GetRequiredService<CronScheduleCache>().ComputeEvaluationFingerprint(timeZoneId: null)
            ))
            .ToArray();

        // No lock configured (default): run the seed directly. Seeded rows carry a DETERMINISTIC primary key derived
        // from the function, so simultaneous first-boot on N nodes converges on a single row (PK dedup) — no duplicate
        // schedules even without the lock. The optional lock below only removes the redundant N-node scan/write storm;
        // it is never the correctness boundary — per-row predicates, node@incarnation ownership, and per-job leases are.
        if (!schedulerOptions.UseStorageLock)
        {
            await internalJobsManager.MigrateDefinedCronJobs(functionsToSeed, cancellationToken).ConfigureAwait(false);
            return;
        }

        IDistributedLease? lease;
        try
        {
            // Resolve the keyed lock lazily INSIDE the try (not via constructor injection) so a consumer factory that
            // throws at resolution — e.g. UseDistributedLock(sp => sp.GetRequiredService<IDistributedLock>()) when no
            // provider is registered — is treated as an acquire fault and skipped, rather than crashing host startup
            // when DI constructs this hosted service.
            var lockProvider = serviceProvider.GetRequiredKeyedService<IDistributedLock>(JobsKeys.LockProvider);
            lease = await lockProvider
                .TryAcquireAsync(JobsKeys.CronSeedMigrationResource, JobsKeys.GuardAcquireOptions, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Host-shutdown / caller cancellation (our own token tripped) must propagate — do not swallow it as a skip.
            throw;
        }
        catch (Exception ex)
        {
            // Lock-store hiccup — including a provider that surfaces an internal timeout as a
            // (Task)OperationCanceledException while our token is NOT cancelled: another node seeds, or the next boot
            // retries. Skip rather than fail startup instead of letting a provider-internal cancel crash host start.
            logger.CronSeedMigrationLockAcquireFailed(ex);
            return;
        }

        if (lease is null)
        {
            // Another node holds the seed lock and is migrating; skip the redundant scan (skip-on-contention).
            logger.CronSeedMigrationSkipped();
            return;
        }

        await using (lease.ConfigureAwait(false))
        {
            await internalJobsManager.MigrateDefinedCronJobs(functionsToSeed, cancellationToken).ConfigureAwait(false);
        }
    }

    private static MissedRunPolicy _ResolveMissedRunPolicy(
        string function,
        MissedRunPolicy? fromAttribute,
        MissedRunPolicy schedulerWide
    )
    {
        var resolved = fromAttribute ?? schedulerWide;
        if (resolved is MissedRunPolicy.Coalesce or MissedRunPolicy.Skip)
        {
            return resolved;
        }

        throw new JobValidatorException(
            string.Create(
                CultureInfo.InvariantCulture,
                $"Missed-run policy value '{(int)resolved}' is not defined for function '{function}'."
            )
        );
    }

    private static int _ResolveGraceSeconds(string function, int? fromAttribute, int schedulerWide)
    {
        var resolved = fromAttribute ?? schedulerWide;
        if (resolved > 0)
        {
            return resolved;
        }

        throw new JobValidatorException(
            string.Create(
                CultureInfo.InvariantCulture,
                $"Missed-run grace must be greater than zero seconds for function '{function}' but was {resolved}."
            )
        );
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
