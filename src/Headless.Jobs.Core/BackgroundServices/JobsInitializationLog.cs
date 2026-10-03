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
