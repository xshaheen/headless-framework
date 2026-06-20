// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.DistributedLocks;

namespace Headless.Jobs.Internal;

internal static class JobsKeys
{
    /// <summary>
    /// Acquire options for the cron-seed migration guard. KTD4: try-once
    /// (<see cref="DistributedLockAcquireOptions.AcquireTimeout"/> = <see cref="TimeSpan.Zero"/>) so a contended node
    /// skips immediately instead of queuing; a generous finite TTL so a holder that dies mid-seed releases via expiry
    /// rather than wedging the resource forever; <see cref="LockMonitoringMode.None"/> because the seed is short and
    /// bounded and nothing observes lease-loss (no <c>LostToken</c>/<c>IsLost</c> read), so a background lease-loss
    /// monitor would be allocated and discarded — the finite TTL is the only safety net. AutoExtend is the escape
    /// hatch if cron-definition counts ever make seeding long-running. The record is immutable, so the single instance
    /// is safe to share across call sites and threads.
    /// </summary>
    public static readonly DistributedLockAcquireOptions GuardAcquireOptions = new()
    {
        AcquireTimeout = TimeSpan.Zero,
        TimeUntilExpires = TimeSpan.FromMinutes(2),
        Monitoring = LockMonitoringMode.None,
    };

    /// <summary>
    /// Keyed-DI service key for the Jobs-scoped <see cref="Headless.DistributedLocks.IDistributedLock"/>.
    /// Keeping the provider under a Jobs-private key means Jobs consumes the app-registered lock the consumer
    /// passes to <c>UseDistributedLock(...)</c> without colliding with any unrelated app-level lock provider.
    /// </summary>
    public const string LockProvider = "headless.jobs";

    /// <summary>
    /// Coarse lock guarding the startup cron-seed migration (<c>MigrateDefinedCronJobs</c>). When held by one node,
    /// other booting nodes skip the redundant scan/upsert. Optimization only — the upsert/constraints remain the
    /// correctness boundary, so a skipped seed is re-run by whichever node owns the lock.
    /// </summary>
    public const string CronSeedMigrationResource = "jobs.cron-seed-migration";
}
