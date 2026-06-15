// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Headless.Coordination;

/// <summary>
/// Drives dead-owner reclaim from the membership substrate: reclaims on <see cref="NodeLeft"/> events and
/// runs a periodic liveness-snapshot reconcile to catch any death missed while not subscribed. The domain
/// reclaim action is supplied by <typeparamref name="TReclaimer"/>.
/// </summary>
/// <remarks>
/// Membership events are best-effort acceleration; the periodic reconcile is the authoritative backstop.
/// Reclaim is idempotent — an in-memory reclaimed-set suppresses duplicate work between the event and
/// reconcile paths, and each consumer's conditional reclaim makes a repeated reclaim a no-op. Reclaim writes
/// use <see cref="CancellationToken.None"/> (KTD6) so a reclaim racing host shutdown is not torn mid-write.
/// A failed reclaim is logged and removed from the set so the next reconcile tick retries it. The closed
/// generic gives each consumer a distinct hosted service and a distinct <see cref="ILogger"/> category.
/// </remarks>
internal sealed class DeadOwnerRecoveryBridge<TReclaimer>(
    INodeMembership membership,
    TReclaimer reclaimer,
    TimeProvider timeProvider,
    ILogger<DeadOwnerRecoveryBridge<TReclaimer>> logger
) : BackgroundService, IDeadOwnerRecoveryBridge
    where TReclaimer : IDeadOwnerReclaimer
{
    private static readonly TimeSpan _WatchRetryInitialBackoff = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan _WatchRetryMaxBackoff = TimeSpan.FromSeconds(30);

    private readonly Lock _gate = new();
    private readonly HashSet<string> _reclaimed = new(StringComparer.Ordinal);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await Task.WhenAll(_WatchLoopAsync(stoppingToken), _ReconcileLoopAsync(stoppingToken)).ConfigureAwait(false);
    }

    private async Task _WatchLoopAsync(CancellationToken stoppingToken)
    {
        // Re-subscribe with bounded exponential backoff so a transient watch failure (store blip, dropped stream)
        // degrades to higher reconcile latency, not to a permanently-dead low-latency path for the process
        // lifetime. The periodic reconcile remains the authoritative backstop throughout.
        var backoff = _WatchRetryInitialBackoff;

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                // The await foreach disposes the underlying enumerator on exit/cancellation, releasing the subscription.
                await foreach (var membershipEvent in membership.WatchAsync(stoppingToken).ConfigureAwait(false))
                {
                    await HandleEventAsync(membershipEvent).ConfigureAwait(false);
                    backoff = _WatchRetryInitialBackoff; // a healthy stream resets the backoff
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return; // expected on host stop
            }
            catch (Exception ex)
            {
                logger.MembershipWatchFailed(ex);
            }

            try
            {
                await timeProvider.Delay(backoff, stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }

            backoff = backoff >= _WatchRetryMaxBackoff ? _WatchRetryMaxBackoff : backoff + backoff;
        }
    }

    private async Task _ReconcileLoopAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await timeProvider.Delay(reclaimer.ReconcileInterval, stoppingToken).ConfigureAwait(false);
                await ReconcileOnceAsync(stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.DeadNodeReconcileFailed(ex);
            }
        }
    }

    /// <summary>Handles a single membership event; only <see cref="NodeLeft"/> triggers reclaim.</summary>
    internal async Task HandleEventAsync(NodeMembershipEvent membershipEvent)
    {
        // NodeSuspected / NodeRecovered / NodeJoined / LocalMembershipLost do not reclaim — only a confirmed leave.
        if (membershipEvent is NodeLeft left)
        {
            await _TryReclaimAsync(left.Identity).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Reconciles dead nodes from the authoritative liveness snapshot, reclaiming any <c>Dead</c> identity not
    /// already handled and pruning the reclaimed-set of identities that have aged out of the snapshot.
    /// </summary>
    internal async Task ReconcileOnceAsync(CancellationToken cancellationToken)
    {
        // Bound the snapshot read so a hung membership store cannot block the reconcile loop indefinitely; on
        // timeout the read fails with OCE, the loop logs DeadNodeReconcileFailed, and the next tick retries. The
        // reconcile interval is the natural cap — a snapshot read should never approach a whole tick.
        var timeout = reclaimer.ReconcileInterval;
        using var timeoutCts = timeout > TimeSpan.Zero ? new CancellationTokenSource(timeout, timeProvider) : null;
        using var linkedCts = timeoutCts is null
            ? null
            : CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token);

        var snapshot = await membership
            .GetLivenessSnapshotAsync(linkedCts?.Token ?? cancellationToken)
            .ConfigureAwait(false);

        var deadOwners = new HashSet<string>(StringComparer.Ordinal);

        foreach (var node in snapshot)
        {
            if (node.State == NodeLivenessState.Dead)
            {
                deadOwners.Add(node.Identity.ToString());
            }
        }

        // Prune reclaimed entries that have left the snapshot so a future same-id incarnation is never suppressed.
        lock (_gate)
        {
            _reclaimed.RemoveWhere(owner => !deadOwners.Contains(owner));
        }

        foreach (var node in snapshot)
        {
            if (node.State == NodeLivenessState.Dead)
            {
                await _TryReclaimAsync(node.Identity).ConfigureAwait(false);
            }
        }
    }

    private async Task _TryReclaimAsync(NodeIdentity identity)
    {
        var owner = identity.ToString();

        bool claimed;

        lock (_gate)
        {
            // Atomically claim this owner; the loser of an event/reconcile race sees false and skips.
            claimed = _reclaimed.Add(owner);
        }

        if (!claimed)
        {
            return;
        }

        try
        {
            // KTD6: reclaim writes are not cancelled by host shutdown; they must complete to avoid a half-reclaim.
            await reclaimer.ReclaimAsync(owner, CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            // Keep the failure observable and let the next reconcile tick retry it (do not silently swallow).
            lock (_gate)
            {
                _reclaimed.Remove(owner);
            }

            logger.DeadNodeReclaimFailed(ex, owner);
        }
    }
}

internal static partial class DeadOwnerRecoveryBridgeLog
{
    [LoggerMessage(
        EventId = 1,
        EventName = "MembershipWatchFailed",
        Level = LogLevel.Error,
        Message = "Membership watch loop failed; dead-owner recovery falls back to the periodic reconcile"
    )]
    public static partial void MembershipWatchFailed(this ILogger logger, Exception exception);

    [LoggerMessage(
        EventId = 2,
        EventName = "DeadNodeReconcileFailed",
        Level = LogLevel.Error,
        Message = "Dead-owner liveness reconcile failed"
    )]
    public static partial void DeadNodeReconcileFailed(this ILogger logger, Exception exception);

    [LoggerMessage(
        EventId = 3,
        EventName = "DeadNodeReclaimFailed",
        Level = LogLevel.Error,
        Message = "Failed to reclaim resources for dead owner {Owner}; will retry on the next reconcile"
    )]
    public static partial void DeadNodeReclaimFailed(this ILogger logger, Exception exception, string owner);
}
