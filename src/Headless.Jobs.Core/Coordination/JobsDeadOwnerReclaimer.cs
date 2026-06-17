// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Coordination;
using Headless.DistributedLocks;
using Headless.Jobs.Interfaces.Managers;
using Headless.Jobs.Internal;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Headless.Jobs.Coordination;

/// <summary>
/// Jobs reclaim sink for the shared <see cref="DeadOwnerRecoveryBridge{TReclaimer}"/>. Releases the
/// operational-store resources owned by a dead node identity; the skip-in-flight policy lives inside
/// <see cref="IInternalJobManager.ReleaseDeadNodeResources"/>.
/// </summary>
internal sealed class JobsDeadOwnerReclaimer(
    IInternalJobManager internalJobManager,
    SchedulerOptionsBuilder optionsBuilder,
    [FromKeyedServices(JobsKeys.LockProvider)] IDistributedLock lockProvider,
    ILogger<JobsDeadOwnerReclaimer> logger
) : IDeadOwnerReclaimer
{
    public TimeSpan ReconcileInterval => optionsBuilder.DeadNodeReconcileInterval;

    public async Task ReclaimAsync(IReadOnlyCollection<string> owners, CancellationToken cancellationToken)
    {
        // No lock configured (default): run the reclaim batch directly. Exact-owner predicates keep repeated reclaim
        // idempotent (a second run touches zero rows), so the lock below is an optimization only (KTD6) — it removes
        // redundant cross-survivor sweeps, never a correctness gate, so the no-lock path is intentionally unchanged.
        if (!optionsBuilder.UseStorageLock)
        {
            await _ReclaimBatchAsync(owners).ConfigureAwait(false);
            return;
        }

        IDistributedLease? lease;
        try
        {
            // Pass the real trigger token to acquire (a cancelled trigger should abort acquisition), but the reclaim
            // body below still uses CancellationToken.None so a sweep racing host shutdown completes.
            lease = await lockProvider
                .TryAcquireAsync(JobsKeys.DeadNodeSweepResource, JobsKeys.GuardAcquireOptions, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // Trigger cancellation must propagate — do not swallow it as a skip.
            throw;
        }
        catch (Exception ex)
        {
            // Lock-store hiccup (KTD6): another survivor sweeps or the next reconcile tick retries. Skip, don't throw.
            logger.DeadNodeSweepLockAcquireFailed(ex);
            return;
        }

        if (lease is null)
        {
            // Another survivor holds the sweep lock and is reclaiming; skip the redundant batch (skip-on-contention).
            logger.DeadNodeSweepSkipped();
            return;
        }

        await using (lease.ConfigureAwait(false))
        {
            await _ReclaimBatchAsync(owners).ConfigureAwait(false);
        }
    }

    private async Task _ReclaimBatchAsync(IReadOnlyCollection<string> owners)
    {
        // Jobs releases per-owner resources, so a batch is a loop. KTD6 / IDeadOwnerReclaimer contract: a reclaim
        // racing host shutdown must complete, so each write uses CancellationToken.None and does not re-thread the
        // incoming token (matches MessagingDeadOwnerReclaimer). The bridge already hands us None; this hardens it.
        foreach (var owner in owners)
        {
            await internalJobManager.ReleaseDeadNodeResources(owner, CancellationToken.None).ConfigureAwait(false);
        }
    }
}

internal static partial class JobsDeadOwnerReclaimerLog
{
    [LoggerMessage(
        EventId = 1,
        EventName = "DeadNodeSweepSkipped",
        Level = LogLevel.Debug,
        Message = "Skipped dead-node reclaim: another survivor holds the '"
            + JobsKeys.DeadNodeSweepResource
            + "' lock and is sweeping."
    )]
    public static partial void DeadNodeSweepSkipped(this ILogger logger);

    [LoggerMessage(
        EventId = 2,
        EventName = "DeadNodeSweepLockAcquireFailed",
        // Warning, not Debug: a contention skip (lease == null) is normal, but an acquire *fault* signals a
        // lock-store problem. The reconcile backstop retries next tick, but a sustained outage must stay visible.
        Level = LogLevel.Warning,
        Message = "Skipped dead-node reclaim: acquiring the '"
            + JobsKeys.DeadNodeSweepResource
            + "' lock failed. Another survivor will sweep or the next reconcile tick will retry."
    )]
    public static partial void DeadNodeSweepLockAcquireFailed(this ILogger logger, Exception exception);
}
