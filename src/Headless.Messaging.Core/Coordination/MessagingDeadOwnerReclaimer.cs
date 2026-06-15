// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Coordination;
using Headless.Messaging.Configuration;
using Headless.Messaging.Internal;
using Headless.Messaging.Persistence;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Headless.Messaging.Coordination;

/// <summary>
/// Messaging reclaim sink for the shared <see cref="DeadOwnerRecoveryBridge{TReclaimer}"/>. Accelerates
/// retry visibility for the outbox (published) and inbox (received) rows still leased by a dead node
/// identity by fast-forwarding their <c>LockedUntil</c>; the conditional, owner-scoped storage UPDATE keeps
/// a repeated reclaim idempotent.
/// </summary>
internal sealed class MessagingDeadOwnerReclaimer(
    IDataStorage storage,
    IOptions<MessagingOptions> options,
    ILogger<MessagingDeadOwnerReclaimer> logger
) : IDeadOwnerReclaimer
{
    public TimeSpan ReconcileInterval => options.Value.DeadNodeReconcileInterval;

    public async Task ReclaimAsync(IReadOnlyCollection<string> owners, CancellationToken cancellationToken)
    {
        // owners is the dead-owner set the bridge surfaced; pass it straight to the owner-scoped conditional
        // UPDATE so a whole reconcile batch collapses into one write per table instead of one per owner.
        // KTD6: a reclaim racing host shutdown must complete to avoid a half-reclaim, so the bridge hands us
        // CancellationToken.None and we deliberately do not re-thread the incoming token into the writes.
        // Failures propagate to the bridge, which logs and re-queues the batch for the next reconcile tick.
        var publishedReclaimed = await storage
            .ReclaimDeadPublishedOwnersAsync(owners, CancellationToken.None)
            .ConfigureAwait(false);

        if (publishedReclaimed > 0 && logger.IsEnabled(LogLevel.Information))
        {
            logger.MessagingDeadOwnerRowsReclaimed("Published", publishedReclaimed);
        }

        var receivedReclaimed = await storage
            .ReclaimDeadReceivedOwnersAsync(owners, CancellationToken.None)
            .ConfigureAwait(false);

        if (receivedReclaimed > 0 && logger.IsEnabled(LogLevel.Information))
        {
            logger.MessagingDeadOwnerRowsReclaimed("Received", receivedReclaimed);
        }
    }
}
