// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Collections.Concurrent;
using System.Data.Common;
using Headless.Checks;
using Headless.Coordination;
using Headless.Fencing;
using Headless.Messaging.Internal;
using Headless.Messaging.Persistence;

namespace Headless.Messaging.Storage.InMemory;

internal sealed partial class InMemoryDataStorage
{
    private static ValueTask<bool> _ReserveAttemptAsync(
        ConcurrentDictionary<Guid, MemoryMessage> messages,
        MediumMessage message,
        int originalInlineAttempts,
        TimeSpan leaseDuration,
        TimeProvider timeProvider,
        CancellationToken cancellationToken
    )
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!messages.TryGetValue(message.StorageId, out var current))
        {
            return ValueTask.FromResult(false);
        }

        lock (current)
        {
            // Revocation may remove the row while this reservation waits for its lock.
            if (!messages.TryGetValue(message.StorageId, out var stored) || !ReferenceEquals(stored, current))
            {
                return ValueTask.FromResult(false);
            }

            if (
                message.Lease.State != RowLeaseState.Active
                || MessageTerminalState.IsTerminal(current.StatusName, current.NextRetryAt)
                || current.Retries != message.Retries
                || current.InlineAttempts != originalInlineAttempts
                || (current.InboxGeneration is not null && !_MatchesInboxFence(current, message.InboxAttemptFence))
            )
            {
                return ValueTask.FromResult(false);
            }

            // The claim leased a whole batch at once and its rows run one after another, so the reservation renews
            // the held generation: a row reserved late in the batch starts its attempt with a full lease.
            var renewed = RowLeaseRules.Renew(
                current.Lease,
                message.Lease.Generation,
                timeProvider.GetUtcNow(),
                leaseDuration
            );
            if (renewed.Status != LeaseRenewalStatus.Renewed)
            {
                return ValueTask.FromResult(false);
            }

            current.Lease = renewed.Next;
            current.InlineAttempts = message.InlineAttempts;
            message.Lease = renewed.Next;
            return ValueTask.FromResult(true);
        }
    }

    /// <summary>
    /// Slides a held lease forward while its attempt runs: only the exact lease generation the caller holds, and only
    /// while it is still live. Like the relational providers, renewal judges the lease alone; the state write that ends
    /// the attempt carries the terminal and inbox guards.
    /// </summary>
    private static ValueTask<bool> _RenewLeaseAsync(
        ConcurrentDictionary<Guid, MemoryMessage> messages,
        MediumMessage message,
        TimeSpan leaseDuration,
        TimeProvider timeProvider,
        CancellationToken cancellationToken
    )
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!messages.TryGetValue(message.StorageId, out var current))
        {
            return ValueTask.FromResult(false);
        }

        lock (current)
        {
            if (!messages.TryGetValue(message.StorageId, out var stored) || !ReferenceEquals(stored, current))
            {
                return ValueTask.FromResult(false);
            }

            if (message.Lease.State != RowLeaseState.Active)
            {
                return ValueTask.FromResult(false);
            }

            var renewed = RowLeaseRules.Renew(
                current.Lease,
                message.Lease.Generation,
                timeProvider.GetUtcNow(),
                leaseDuration
            );
            if (renewed.Status != LeaseRenewalStatus.Renewed)
            {
                return ValueTask.FromResult(false);
            }

            current.Lease = renewed.Next;
            message.Lease = message.Lease with { ExpiresAt = renewed.Next.ExpiresAt };
            return ValueTask.FromResult(true);
        }
    }

    private static ValueTask<bool> _LeaseAndReserveAttemptAsync(
        ConcurrentDictionary<Guid, MemoryMessage> messages,
        MediumMessage message,
        TimeSpan leaseDuration,
        int originalInlineAttempts,
        TimeProvider timeProvider,
        string? holder,
        IGuidGenerator? fenceGenerator,
        CancellationToken cancellationToken
    )
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!messages.TryGetValue(message.StorageId, out var current))
        {
            return ValueTask.FromResult(false);
        }

        lock (current)
        {
            // A reference acquired before revocation is not a live durable row.
            if (!messages.TryGetValue(message.StorageId, out var stored) || !ReferenceEquals(stored, current))
            {
                return ValueTask.FromResult(false);
            }

            // Fresh-dispatch fast path: lease acquisition + attempt reservation in one atomic step.
            // Combines _LeaseAsync's lease-contention guard with _ReserveAttemptAsync's durable
            // counter CAS; no holder match is required because this path is TAKING the lease.
            if (
                MessageTerminalState.IsTerminal(current.StatusName, current.NextRetryAt)
                || current.Retries != message.Retries
                || current.InlineAttempts != originalInlineAttempts
                || !_TryGrantLease(current, timeProvider.GetUtcNow(), leaseDuration, holder)
            )
            {
                return ValueTask.FromResult(false);
            }

            current.InlineAttempts = message.InlineAttempts;
            message.Lease = current.Lease;
            _AllocateInboxFence(current, message, fenceGenerator);
            return ValueTask.FromResult(true);
        }
    }

    private static void _AllocateInboxFence(
        MemoryMessage current,
        MediumMessage message,
        IGuidGenerator? fenceGenerator
    )
    {
        if (current.InboxGeneration is null || current.Lease.State != RowLeaseState.Active || fenceGenerator is null)
        {
            return;
        }

        var fence = new InboxAttemptFence(
            current.StorageId,
            current.Lane,
            current.InboxGeneration.Number,
            current.InboxGeneration.IncarnationId,
            fenceGenerator.Create(),
            current.Lease.Generation
        );
        current.InboxAttemptFence = fence;
        message.InboxAttemptFence = fence;
    }

    public ValueTask<int> DeleteReceivedMessageAsync(Guid id, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_receivedUpsertLock)
        {
            if (ReceivedMessages.TryRemove(id, out var removed))
            {
                _RemoveFromIdentityIndex(removed);
                return ValueTask.FromResult(1);
            }

            return ValueTask.FromResult(0);
        }
    }

    public ValueTask<int> DeleteReceivedMessagesAsync(
        IReadOnlyList<Guid> ids,
        CancellationToken cancellationToken = default
    )
    {
        cancellationToken.ThrowIfCancellationRequested();
        var deleted = 0;

        lock (_receivedUpsertLock)
        {
            foreach (var id in ids)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (ReceivedMessages.TryRemove(id, out var removed))
                {
                    _RemoveFromIdentityIndex(removed);
                    deleted++;
                }
            }
        }

        return ValueTask.FromResult(deleted);
    }

    private void _RemoveFromIdentityIndex(MemoryMessage removed)
    {
        if (
            removed.InboxKey is not null
            && _inboxIdentityIndex.TryGetValue(removed.InboxKey, out var indexedId)
            && indexedId == removed.StorageId
        )
        {
            _inboxIdentityIndex.Remove(removed.InboxKey);
        }

        if (removed.Origin.Headers.TryGetValue(Headers.MessageId, out var messageId) && messageId is not null)
        {
            _receivedIdentityIndex.TryRemove(
                (removed.Version, messageId, removed.ConsumerIdentity, removed.Lane),
                out _
            );
        }
    }

    public ValueTask<int> DeletePublishedMessageAsync(Guid id, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var deleteResult = PublishedMessages.TryRemove(id, out _);
        return ValueTask.FromResult(deleteResult ? 1 : 0);
    }

    public ValueTask<int> DeletePublishedMessagesAsync(
        IReadOnlyList<Guid> ids,
        CancellationToken cancellationToken = default
    )
    {
        cancellationToken.ThrowIfCancellationRequested();
        var deleted = 0;

        foreach (var id in ids)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (PublishedMessages.TryRemove(id, out _))
            {
                deleted++;
            }
        }

        return ValueTask.FromResult(deleted);
    }

    public ValueTask<IReadOnlyList<Guid>> RequeuePublishedMessagesAsync(
        IReadOnlyList<Guid> ids,
        CancellationToken cancellationToken = default
    )
    {
        Argument.IsNotNull(ids);
        cancellationToken.ThrowIfCancellationRequested();

        var requeued = new List<Guid>(ids.Count);
        foreach (var id in ids.Distinct())
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!PublishedMessages.TryGetValue(id, out var row))
            {
                continue;
            }

            lock (row)
            {
                if (row.StatusName != StatusName.Failed || row.NextRetryAt is not null)
                {
                    continue;
                }

                row.NextRetryAt = timeProvider.GetUtcNow();
                row.Retries = 0;
                row.InlineAttempts = 0;
                row.ExpiresAt = null;
                row.Lease = RowLeaseRules.Revoke(row.Lease).Next;
            }

            requeued.Add(id);
        }

        return ValueTask.FromResult<IReadOnlyList<Guid>>(requeued);
    }

    public ValueTask ScheduleMessagesOfDelayedAsync(
        Func<DbTransaction?, IEnumerable<MediumMessage>, ValueTask> scheduleTask,
        CancellationToken cancellationToken = default
    )
    {
        cancellationToken.ThrowIfCancellationRequested();
        var version = messagingOptions.Value.Version;
        var result = PublishedMessages
            .Values.Where(x =>
                string.Equals(x.Version, version, StringComparison.Ordinal)
                && _IsSupportedLane(x.Lane)
                && (
                    (x.StatusName == StatusName.Delayed && x.ExpiresAt < timeProvider.GetUtcNow().AddMinutes(2))
                    || (x.StatusName == StatusName.Queued && x.ExpiresAt < timeProvider.GetUtcNow().AddMinutes(-1))
                )
            )
            .Take(messagingOptions.Value.SchedulerBatchSize)
            .Cast<MediumMessage>();

        return scheduleTask(null, result);
    }

    public ValueTask<IReadOnlyList<MediumMessage>> ClaimDelayedMessagesAsync(
        CancellationToken cancellationToken = default
    )
    {
        cancellationToken.ThrowIfCancellationRequested();

        var now = timeProvider.GetUtcNow();
        var delayedBefore = now.AddMinutes(2);
        var queuedBefore = now.AddMinutes(-1);
        var holder = nodeMembership.GetLeaseHolder();
        var version = messagingOptions.Value.Version;
        var batchSize = messagingOptions.Value.SchedulerBatchSize;
        var claimed = new List<MediumMessage>(batchSize);

        var candidates = PublishedMessages
            .Values.Where(message =>
                string.Equals(message.Version, version, StringComparison.Ordinal)
                && _IsSupportedLane(message.Lane)
                && message.ExpiresAt is not null
                && (
                    (message.StatusName == StatusName.Delayed && message.ExpiresAt < delayedBefore)
                    || (message.StatusName == StatusName.Queued && message.ExpiresAt < queuedBefore)
                )
            )
            .OrderBy(message => message.ExpiresAt)
            .ThenBy(message => message.StorageId);

        foreach (var candidate in candidates)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                if (claimed.Count == 0)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                }

                break;
            }

            if (claimed.Count == batchSize)
            {
                break;
            }

            lock (candidate)
            {
                if (
                    !string.Equals(candidate.Version, version, StringComparison.Ordinal)
                    || !_IsSupportedLane(candidate.Lane)
                    || candidate.ExpiresAt is null
                    || MessageTerminalState.IsTerminal(candidate.StatusName, candidate.NextRetryAt)
                    || (
                        (candidate.StatusName != StatusName.Delayed || candidate.ExpiresAt >= delayedBefore)
                        && (candidate.StatusName != StatusName.Queued || candidate.ExpiresAt >= queuedBefore)
                    )
                )
                {
                    continue;
                }

                // The lease runs to the due time plus the dispatch timeout, so a row claimed ahead of its due time
                // stays held until it is sent.
                var leaseStart = candidate.ExpiresAt > now ? candidate.ExpiresAt.Value : now;
                var leaseDuration = leaseStart - now + messagingOptions.Value.RetryPolicy.DispatchTimeout;
                if (!_TryGrantLease(candidate, now, leaseDuration, holder))
                {
                    continue;
                }

                candidate.StatusName = StatusName.Queued;
                claimed.Add(_ToSnapshot(candidate));
            }
        }

        return ValueTask.FromResult<IReadOnlyList<MediumMessage>>(claimed);
    }

    public IMonitoringApi GetMonitoringApi()
    {
        return new InMemoryMonitoringApi(this, timeProvider);
    }
}
