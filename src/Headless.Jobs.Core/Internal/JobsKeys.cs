// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Jobs.Internal;

internal static class JobsKeys
{
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

    /// <summary>
    /// Coarse lock guarding the dead-owner resource reclaim batch (<c>ReleaseDeadNodeResources</c>). When held by one
    /// survivor, other survivors skip the redundant sweep. Optimization only — exact-owner predicates keep repeated
    /// reclaim idempotent, so a skipped sweep is covered by whichever node owns the lock or the next reconcile tick.
    /// </summary>
    public const string DeadNodeSweepResource = "jobs.dead-node-sweep";
}
