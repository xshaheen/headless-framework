// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Runtime.ExceptionServices;
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
    ILogger<MessagingDeadOwnerReclaimer> logger,
    MessagingOutboxes? outboxes = null
) : IDeadOwnerReclaimer
{
    public TimeSpan ReconcileInterval => options.Value.DeadNodeReconcileInterval;

    public async Task ReclaimAsync(IReadOnlyCollection<string> owners, CancellationToken cancellationToken)
    {
        // owners is the dead-owner set the bridge surfaced; pass it straight to the owner-scoped conditional
        // UPDATE so a whole reconcile batch collapses into one write per table instead of one per owner.
        // A reclaim racing host shutdown must complete to avoid a half-reclaim, so the bridge hands us
        // CancellationToken.None and we deliberately do not re-thread the incoming token into the writes.
        // Every table is attempted even when an earlier one fails, so one unreachable outbox database does not
        // hold back the others; the failures then propagate to the bridge, which logs and re-queues the batch
        // for the next reconcile tick, and the idempotent UPDATE makes repeating the tables that succeeded safe.
        List<Exception>? failures = null;

        await tryAsync(() => _ReclaimPublishedAsync(storage, "Published", owners)).ConfigureAwait(false);
        await tryAsync(async () =>
            {
                var receivedReclaimed = await storage
                    .ReclaimDeadReceivedOwnersAsync(owners, CancellationToken.None)
                    .ConfigureAwait(false);

                if (receivedReclaimed > 0 && logger.IsEnabled(LogLevel.Information))
                {
                    logger.MessagingDeadOwnerRowsReclaimed("Received", receivedReclaimed);
                }
            })
            .ConfigureAwait(false);

        foreach (var outbox in outboxes?.Secondaries ?? [])
        {
            await tryAsync(() => _ReclaimPublishedAsync(outbox.Storage, $"Published ({outbox.Name})", owners))
                .ConfigureAwait(false);
        }

        switch (failures)
        {
            case null:
                return;
            case [var single]:
                ExceptionDispatchInfo.Throw(single);
                break;
            default:
                throw new AggregateException(failures);
        }

        async Task tryAsync(Func<Task> reclaim)
        {
            try
            {
                await reclaim().ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                (failures ??= []).Add(ex);
            }
        }
    }

    private async Task _ReclaimPublishedAsync(IDataStorage target, string kind, IReadOnlyCollection<string> owners)
    {
        var publishedReclaimed = await target
            .ReclaimDeadPublishedOwnersAsync(owners, CancellationToken.None)
            .ConfigureAwait(false);

        if (publishedReclaimed > 0 && logger.IsEnabled(LogLevel.Information))
        {
            logger.MessagingDeadOwnerRowsReclaimed(kind, publishedReclaimed);
        }
    }
}
