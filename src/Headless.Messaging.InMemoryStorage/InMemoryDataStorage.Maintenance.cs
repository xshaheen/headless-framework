// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Collections.Concurrent;
using Headless.Messaging.Internal;
using Headless.Messaging.Messages;
using Headless.Messaging.Monitoring;

namespace Headless.Messaging.InMemoryStorage;

internal sealed partial class InMemoryDataStorage
{
    private static ValueTask<bool> _ReserveAttemptAsync(
        ConcurrentDictionary<Guid, MemoryMessage> messages,
        MediumMessage message,
        int originalInlineAttempts,
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
            if (
                ((current.StatusName is StatusName.Succeeded or StatusName.Failed) && current.NextRetryAt is null)
                || current.Retries != message.Retries
                || current.InlineAttempts != originalInlineAttempts
                || current.LockedUntil != message.LockedUntil
                || !string.Equals(current.Owner, message.Owner, StringComparison.Ordinal)
                || current.LockedUntil is null
                || current.LockedUntil <= timeProvider.GetUtcNow().UtcDateTime
            )
            {
                return ValueTask.FromResult(false);
            }

            current.InlineAttempts = message.InlineAttempts;
            return ValueTask.FromResult(true);
        }
    }

    private static ValueTask<bool> _LeaseAndReserveAttemptAsync(
        ConcurrentDictionary<Guid, MemoryMessage> messages,
        MediumMessage message,
        TimeSpan leaseDuration,
        int originalInlineAttempts,
        TimeProvider timeProvider,
        string? owner,
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
            // Fresh-dispatch fast path: lease acquisition + attempt reservation in one atomic step.
            // Combines _LeaseAsync's lease-contention guard with _ReserveAttemptAsync's durable
            // counter CAS; no owner match is required because this path is TAKING the lease.
            var nowUtc = timeProvider.GetUtcNow().UtcDateTime;
            if (
                (current.StatusName is StatusName.Succeeded or StatusName.Failed) && current.NextRetryAt is null
                || current.Retries != message.Retries
                || current.InlineAttempts != originalInlineAttempts
                || (current.LockedUntil is not null && current.LockedUntil > nowUtc)
            )
            {
                return ValueTask.FromResult(false);
            }

            var lockedUntil = nowUtc.Add(leaseDuration);
            current.LockedUntil = lockedUntil;
            current.Owner = owner;
            current.InlineAttempts = message.InlineAttempts;
            message.LockedUntil = lockedUntil;
            message.Owner = owner;
            return ValueTask.FromResult(true);
        }
    }

    public ValueTask<int> DeleteReceivedMessageAsync(Guid id, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (ReceivedMessages.TryRemove(id, out var removed))
        {
            _RemoveFromIdentityIndex(removed);
            return ValueTask.FromResult(1);
        }
        return ValueTask.FromResult(0);
    }

    public ValueTask<int> DeleteReceivedMessagesAsync(
        IReadOnlyList<Guid> ids,
        CancellationToken cancellationToken = default
    )
    {
        cancellationToken.ThrowIfCancellationRequested();
        var deleted = 0;

        foreach (var id in ids)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (ReceivedMessages.TryRemove(id, out var removed))
            {
                _RemoveFromIdentityIndex(removed);
                deleted++;
            }
        }

        return ValueTask.FromResult(deleted);
    }

    private void _RemoveFromIdentityIndex(MemoryMessage removed)
    {
        if (removed.Origin.Headers.TryGetValue(Headers.MessageId, out var messageId) && messageId is not null)
        {
            _receivedIdentityIndex.TryRemove((removed.Version, messageId, removed.Group, removed.IntentType), out _);
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

    public ValueTask ScheduleMessagesOfDelayedAsync(
        Func<object?, IEnumerable<MediumMessage>, ValueTask> scheduleTask,
        CancellationToken cancellationToken = default
    )
    {
        cancellationToken.ThrowIfCancellationRequested();
        var version = messagingOptions.Value.Version;
        var result = PublishedMessages
            .Values.Where(x =>
                string.Equals(x.Version, version, StringComparison.Ordinal)
                && (
                    (
                        x.StatusName == StatusName.Delayed
                        && x.ExpiresAt < timeProvider.GetUtcNow().UtcDateTime.AddMinutes(2)
                    )
                    || (
                        x.StatusName == StatusName.Queued
                        && x.ExpiresAt < timeProvider.GetUtcNow().UtcDateTime.AddMinutes(-1)
                    )
                )
            )
            .Take(messagingOptions.Value.SchedulerBatchSize)
            .Cast<MediumMessage>();

        return scheduleTask(null, result);
    }

    public IMonitoringApi GetMonitoringApi() => new InMemoryMonitoringApi(this, timeProvider);
}
