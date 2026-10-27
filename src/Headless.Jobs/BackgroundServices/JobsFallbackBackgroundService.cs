// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Coordination;
using Headless.Jobs.Coordination;
using Headless.Jobs.Internal;
using Headless.Jobs.JobsThreadPool;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Headless.Jobs.BackgroundServices;

internal sealed class JobsFallbackBackgroundService(
    IInternalJobManager internalJobsManager,
    SchedulerOptionsBuilder schedulerOptions,
    JobsExecutionTaskHandler tickerExecutionTaskHandler,
    JobsTaskScheduler jobsTaskScheduler,
    IJobFunctionConcurrencyGate concurrencyGate,
    JobFunctionRegistry functionRegistry,
    TimeProvider timeProvider,
    IJobsOwnerIdentity ownerIdentity,
    JobsActivationBarrier activationBarrier,
    ILogger<JobsFallbackBackgroundService> logger,
    INodeMembership? membership = null,
    JobsDeadOwnerReclaimer? deadOwnerReclaimer = null
) : BackgroundService
{
    private int _started;
    private long _lastOrphanSweepTimestamp;
    private long _lastStallSweepTimestamp;
    private readonly TimeSpan _fallbackJobPeriod = schedulerOptions.FallbackIntervalChecker;

    // One cadence with the dead-owner bridge: the reclaimer resolves and logs it; a host without the reclaimer (the
    // in-memory path, which never sweeps) falls back to the same resolution without coordination options.
    private readonly TimeSpan _orphanSweepInterval =
        deadOwnerReclaimer?.ReconcileInterval
        ?? JobsDeadOwnerReclaimer.ResolveReconcileInterval(
            schedulerOptions.DeadNodeReconcileInterval,
            coordination: null,
            logger
        );

    public override Task StartAsync(CancellationToken ct)
    {
        return Interlocked.CompareExchange(ref _started, 1, 0) != 0 ? Task.CompletedTask : base.StartAsync(ct);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Fail-stop, mirroring the main scheduler loop: the reclaim sweep applies cluster-wide terminal
        // transitions (MarkFailed/Skip), so a node that lost coordination membership must stop sweeping other
        // live nodes' rows — under StopMembershipOnly nothing else would stop this loop. On the in-memory path
        // the token is None and never fires.
        using var membershipLinkedCts = CancellationTokenSource.CreateLinkedTokenSource(
            stoppingToken,
            ownerIdentity.MembershipLostToken
        );
        var loopToken = membershipLinkedCts.Token;

        // Same activation gate as the main scheduler loop: RunTimedOutTickers re-queues and dispatches cron
        // occurrences, so it must not select before the fingerprint drain has published one stable snapshot. The
        // barrier — not hosted-service start order — is what holds while the drain runs after the host start.
        Exception? activationFailure;
        try
        {
            activationFailure = await activationBarrier.WaitAsync(loopToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // Same diagnostic the tail below emits: under StopMembershipOnly the process keeps running with no
            // reclaim sweeps, and losing membership while still parked here must not be the one silent path.
            _LogMembershipLossIfLost(stoppingToken);

            return;
        }

        if (activationFailure is not null)
        {
            logger.LogJobsFallbackStoppedOnActivationFailure(activationFailure);

            return;
        }

        while (!loopToken.IsCancellationRequested)
        {
            try
            {
                // If the scheduler is frozen or disposed (e.g., manual start mode or shutdown),
                // skip queuing fallback work to avoid throwing and stopping the host.
                if (jobsTaskScheduler.IsFrozen || jobsTaskScheduler.IsDisposed)
                {
                    await timeProvider.Delay(_fallbackJobPeriod, loopToken).ConfigureAwait(false);
                    continue;
                }

                // #316: reclaim jobs stalled InProgress with a lapsed lease before re-queuing timed-out work, so
                // a Retry row released to Idle here is picked up by RunTimedOutTickers in the same tick. Closes the
                // gap where a job wedged on a still-live node is reclaimed by neither the claim predicate nor the
                // dead-node sweep.
                await _ReclaimStalledResourcesAsync(loopToken).ConfigureAwait(false);
                await _ReclaimOrphanedOwnersAsync(loopToken).ConfigureAwait(false);

                var functions = await internalJobsManager.RunTimedOutTickers(loopToken).ConfigureAwait(false);

                if (functions.Length != 0)
                {
                    foreach (var function in functions)
                    {
                        // Attach cached delegates to the whole hydrated tree, not just the grandchild level, so a
                        // chain deeper than three levels also executes its tail on the timed-out fallback path.
                        // Runs before the dispatch sort below because it also stamps CachedPriority.
                        JobsExecutionContext.CacheFunctionReferences(function, functionRegistry);
                    }

                    foreach (var function in functions.OrderBy(x => x.CachedPriority.DispatchRank()))
                    {
                        var semaphore = concurrencyGate.GetSemaphoreOrNull(
                            function.FunctionName,
                            function.CachedMaxConcurrency
                        );

                        try
                        {
                            // The loop token governs only admission. A dispatched job follows the task scheduler's
                            // execution lifetime, like immediate dispatch: this service's StopAsync cancels the loop
                            // token as soon as the host stop begins, so running on it would cut the job off instead of
                            // letting the scheduler drain it within the stop budget. Membership loss still reaches a
                            // running job as lease loss through the handler's own MembershipLostToken registration.
                            await jobsTaskScheduler
                                .QueueAsync(
                                    JobsAdmissionWorkItem.Create(
                                        jobsTaskScheduler,
                                        internalJobsManager,
                                        tickerExecutionTaskHandler,
                                        logger,
                                        function,
                                        isDue: true
                                    ),
                                    function.CachedPriority,
                                    capacityCancellationToken: loopToken,
                                    executionCancellationToken: jobsTaskScheduler.ExecutionCancellationToken,
                                    concurrencyGate: semaphore
                                )
                                .ConfigureAwait(false);
                        }
                        catch (InvalidOperationException)
                            when (jobsTaskScheduler.IsFrozen || jobsTaskScheduler.IsDisposed)
                        {
                            // Scheduler is frozen/disposed – ignore and let loop delay
                            break;
                        }
                    }

                    await timeProvider.Delay(TimeSpan.FromMilliseconds(10), loopToken).ConfigureAwait(false);
                }
                else
                {
                    await timeProvider.Delay(_fallbackJobPeriod, loopToken).ConfigureAwait(false);
                }
            }
            catch (OperationCanceledException) when (loopToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                // Swallow unexpected exceptions so they don't bubble up
                // and stop the host; wait a bit before retrying.
                logger.LogJobsFallbackTickFailed(exception, _fallbackJobPeriod);
                await timeProvider.Delay(_fallbackJobPeriod, loopToken).ConfigureAwait(false);
            }
        }

        _LogMembershipLossIfLost(stoppingToken);
    }

    // Shared by the loop tail and the activation wait so neither path can exit membership-lost without a diagnostic.
    // Host shutdown stays quiet here; only the membership-loss exit is permanent while the process keeps running.
    private void _LogMembershipLossIfLost(CancellationToken stoppingToken)
    {
        if (ownerIdentity.MembershipLostToken.IsCancellationRequested && !stoppingToken.IsCancellationRequested)
        {
            logger.LogJobsFallbackStoppedOnMembershipLoss();
        }
    }

    // While timed-out work drains, the loop spins every 10 ms, and an ungated sweep would pay two stall scans and a
    // cluster-wide terminal-children reconcile on every spin. A stalled row only becomes reclaimable after its lease
    // lapses, and an idle loop already sweeps once per period, so sweeping once per period during a drain keeps the
    // same recovery bound. Stamped before the call so a failing sweep is not retried on the next spin either.
    private async Task _ReclaimStalledResourcesAsync(CancellationToken cancellationToken)
    {
        if (_lastStallSweepTimestamp != 0 && timeProvider.GetElapsedTime(_lastStallSweepTimestamp) < _fallbackJobPeriod)
        {
            return;
        }

        _lastStallSweepTimestamp = timeProvider.GetTimestamp();

        await internalJobsManager.ReclaimStalledResources(cancellationToken).ConfigureAwait(false);
    }

    // Recovers rows stamped by an owner identity that is no longer a live member: a Dead identity, a superseded
    // incarnation (its successor's registration instantly filters it from every liveness snapshot, so it never
    // classifies Dead and the dead-owner bridge never sees it), or a dead identity pruned past its retention window.
    // Dead owners are included because the bridge reclaims each one once: a reclaim that ran before every row was
    // stamped, or a missed NodeLeft on Redis (which keeps Dead identities for RedisKnownNodeRetention, 7 days by
    // default), would otherwise strand rows that long. Idle/Queued rows with a null ExecutionTime (non-timed chain
    // descendants) are matched by no other sweep in that state — this reconcile is their only recovery path, per the
    // coordination contract ("consumers must also periodically reconcile rows whose owner identity is not live").
    //
    // The stamped-owner scan is read BEFORE the snapshot: stamping requires established membership, so every
    // owner in the scan registered no later than the scan, and one that is still live must appear Alive or Suspected
    // in the later snapshot. The reverse order is unsafe: a node that registers and stamps between the two reads would
    // be present in the scan but absent from the earlier snapshot, and this iteration acts on this iteration's diff,
    // so it would be reclaimed while alive. Suspected identities are excluded: they may still be alive and renewing.
    private async Task _ReclaimOrphanedOwnersAsync(CancellationToken cancellationToken)
    {
        if (membership is null)
        {
            // In-memory path: single-process ownership with no incarnations — nothing can be orphaned.
            return;
        }

        if (
            _lastOrphanSweepTimestamp != 0
            && timeProvider.GetElapsedTime(_lastOrphanSweepTimestamp) < _orphanSweepInterval
        )
        {
            return;
        }

        _lastOrphanSweepTimestamp = timeProvider.GetTimestamp();

        var stamped = await internalJobsManager.GetActiveOwnerIdsAsync(cancellationToken).ConfigureAwait(false);

        if (stamped.Length == 0)
        {
            return;
        }

        var snapshot = await membership.GetLivenessSnapshotAsync(cancellationToken).ConfigureAwait(false);
        var live = snapshot
            .Where(static x => x.State is NodeLivenessState.Alive or NodeLivenessState.Suspected)
            .Select(static x => x.Identity.ToString())
            .ToHashSet(StringComparer.Ordinal);
        List<string> orphanedOwners = [];

        foreach (var owner in stamped)
        {
            if (live.Contains(owner))
            {
                continue;
            }

            // Protective self-exclusion for registration gaps; a live self is in the set anyway.
            if (ownerIdentity.TryGetStampOwner(out var self) && string.Equals(owner, self, StringComparison.Ordinal))
            {
                continue;
            }

            orphanedOwners.Add(owner);
        }

        if (orphanedOwners.Count == 0 || ownerIdentity.MembershipLostToken.IsCancellationRequested)
        {
            return;
        }

        foreach (var owner in orphanedOwners)
        {
            logger.LogJobsOrphanedOwnerReclaimed(owner);
        }

        // Final membership fence immediately before the must-complete durable transition. Once started, the release
        // deliberately uses None so host shutdown cannot interrupt only part of the owner batch.
        await internalJobsManager
            .ReleaseDeadNodeResources(orphanedOwners, CancellationToken.None)
            .ConfigureAwait(false);
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        Interlocked.Exchange(ref _started, 0);
        await base.StopAsync(cancellationToken).ConfigureAwait(false);
    }
}

internal static partial class JobsFallbackBackgroundServiceLog
{
    [LoggerMessage(
        EventId = 3200,
        Level = LogLevel.Warning,
        Message = "Jobs fallback tick failed; the service will retry after {FallbackPeriod}."
    )]
    internal static partial void LogJobsFallbackTickFailed(
        this ILogger logger,
        Exception exception,
        TimeSpan fallbackPeriod
    );

    [LoggerMessage(
        EventId = 3201,
        Level = LogLevel.Warning,
        Message = "Jobs fallback service stopped because local coordination membership was lost; "
            + "reclaim sweeps and timed-out re-dispatch no longer run on this node."
    )]
    internal static partial void LogJobsFallbackStoppedOnMembershipLoss(this ILogger logger);

    [LoggerMessage(
        EventId = 3202,
        Level = LogLevel.Warning,
        Message = "Reclaiming rows stamped by owner '{Owner}', which is neither Alive nor Suspected in the coordination "
            + "liveness snapshot (dead, superseded, or pruned identity)."
    )]
    internal static partial void LogJobsOrphanedOwnerReclaimed(this ILogger logger, string owner);

    [LoggerMessage(
        EventId = 3203,
        Level = LogLevel.Warning,
        Message = "Jobs fallback service stopped because startup activation failed; reclaim sweeps and timed-out "
            + "re-dispatch will not run on this node. Host startup fails with the underlying error."
    )]
    internal static partial void LogJobsFallbackStoppedOnActivationFailure(this ILogger logger, Exception exception);
}
