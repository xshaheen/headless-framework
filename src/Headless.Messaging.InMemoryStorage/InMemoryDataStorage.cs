// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Collections.Concurrent;
using Headless.Abstractions;
using Headless.Messaging.Configuration;
using Headless.Messaging.Internal;
using Headless.Messaging.Messages;
using Headless.Messaging.Monitoring;
using Headless.Messaging.Persistence;
using Headless.Messaging.Serialization;
using Microsoft.Extensions.Options;

namespace Headless.Messaging.InMemoryStorage;

internal sealed class InMemoryDataStorage(
    IOptions<MessagingOptions> messagingOptions,
    ISerializer serializer,
    ILongIdGenerator longIdGenerator,
    TimeProvider timeProvider
) : IDataStorage
{
    public ConcurrentDictionary<long, MemoryMessage> PublishedMessages { get; } = new();

    public ConcurrentDictionary<long, MemoryMessage> ReceivedMessages { get; } = new();

    internal ConcurrentDictionary<string, (string Instance, DateTime ExpiresAt)> Locks { get; } =
        new(StringComparer.Ordinal);

    // Secondary index keyed on the SQL-providers' upsert identity (Version, MessageId, Group?).
    // Maps to the primary row id in <see cref="ReceivedMessages"/>. The lookup that backs
    // StoreReceivedExceptionMessageAsync is then O(1) via TryGetValue instead of an O(N) scan
    // over the whole received-message map. Updated in lockstep with every code path that inserts
    // into or removes from ReceivedMessages. ValueTuple's default equality uses ordinal string
    // equality for each component, matching the SQL providers' BINARY-collation key semantics.
    private readonly ConcurrentDictionary<
        (string Version, string MessageId, string? Group),
        long
    > _receivedIdentityIndex = new();

    // Serializes the lookup-then-insert/update paths in BOTH StoreReceivedExceptionMessageAsync
    // and StoreReceivedMessageAsync so two concurrent broker redeliveries (or two concurrent first
    // arrivals via the consume path) cannot both decide "not found" and race to insert duplicate
    // rows for the same (Version, MessageId, Group) tuple. Renamed from _receivedExceptionUpsertLock
    // when the consume path adopted the same check-then-insert pattern in R3.
    private readonly Lock _receivedUpsertLock = new();

    public void Clear()
    {
        PublishedMessages.Clear();
        ReceivedMessages.Clear();
        _receivedIdentityIndex.Clear();
        Locks.Clear();
    }

    public ValueTask<bool> AcquireLockAsync(
        string key,
        TimeSpan ttl,
        string instance,
        CancellationToken cancellationToken = default
    )
    {
        cancellationToken.ThrowIfCancellationRequested();

        var now = timeProvider.GetUtcNow().UtcDateTime;
        var expiresAt = now.Add(ttl);

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (!Locks.TryGetValue(key, out var current))
            {
                if (Locks.TryAdd(key, (instance, expiresAt)))
                {
                    return ValueTask.FromResult(true);
                }

                continue;
            }

            if (current.ExpiresAt > now)
            {
                return ValueTask.FromResult(false);
            }

            if (Locks.TryUpdate(key, (instance, expiresAt), current))
            {
                return ValueTask.FromResult(true);
            }
        }
    }

    public ValueTask ReleaseLockAsync(string key, string instance, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        while (Locks.TryGetValue(key, out var current))
        {
            if (!string.Equals(current.Instance, instance, StringComparison.Ordinal))
            {
                return ValueTask.CompletedTask;
            }

            if (Locks.TryUpdate(key, (string.Empty, DateTime.MinValue), current))
            {
                return ValueTask.CompletedTask;
            }
        }

        return ValueTask.CompletedTask;
    }

    public ValueTask RenewLockAsync(
        string key,
        TimeSpan ttl,
        string instance,
        CancellationToken cancellationToken = default
    )
    {
        cancellationToken.ThrowIfCancellationRequested();

        while (Locks.TryGetValue(key, out var current))
        {
            if (!string.Equals(current.Instance, instance, StringComparison.Ordinal))
            {
                return ValueTask.CompletedTask;
            }

            if (Locks.TryUpdate(key, (instance, current.ExpiresAt.Add(ttl)), current))
            {
                return ValueTask.CompletedTask;
            }
        }

        return ValueTask.CompletedTask;
    }

    public ValueTask ChangePublishStateToDelayedAsync(long[] storageIds, CancellationToken cancellationToken = default)
    {
        foreach (var storageId in storageIds)
        {
            cancellationToken.ThrowIfCancellationRequested();
            PublishedMessages[storageId].StatusName = StatusName.Delayed;
        }

        return ValueTask.CompletedTask;
    }

    public ValueTask<bool> ChangePublishStateAsync(
        MediumMessage message,
        StatusName state,
        object? dbTransaction = null,
        DateTime? nextRetryAt = null,
        DateTime? lockedUntil = null,
        int? originalRetries = null,
        CancellationToken cancellationToken = default
    )
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (!PublishedMessages.TryGetValue(message.StorageId, out var current))
        {
            return ValueTask.FromResult(false);
        }

        bool updated;
        lock (current)
        {
            // Mirror the SQL providers' terminal guard: only reject when status is terminal AND
            // NextRetryAt is null. A Succeeded row with non-null NextRetryAt is degenerate but
            // shouldn't be blocked by this guard — cross-storage parity per the at-least-once contract.
            if (
                (current.StatusName is StatusName.Succeeded || current.StatusName is StatusName.Failed)
                && current.NextRetryAt is null
            )
            {
                return ValueTask.FromResult(false);
            }

            if (originalRetries.HasValue && current.Retries != originalRetries.Value)
            {
                return ValueTask.FromResult(false);
            }

            var utcNextRetryAt = nextRetryAt.ToUtcOrSelf();
            var utcLockedUntil = lockedUntil.ToUtcOrSelf();
            current.StatusName = state;
            current.ExpiresAt = message.ExpiresAt;
            current.NextRetryAt = utcNextRetryAt;
            current.LockedUntil = utcLockedUntil;
            current.Retries = message.Retries;
            current.Content = serializer.Serialize(message.Origin);
            updated = true;
        }

        return ValueTask.FromResult(updated);
    }

    public ValueTask<bool> LeasePublishAsync(
        MediumMessage message,
        DateTime lockedUntil,
        CancellationToken cancellationToken = default
    ) => _LeaseAsync(PublishedMessages, message, lockedUntil, cancellationToken);

    public ValueTask<bool> ChangeReceiveStateAsync(
        MediumMessage message,
        StatusName state,
        DateTime? nextRetryAt = null,
        DateTime? lockedUntil = null,
        int? originalRetries = null,
        CancellationToken cancellationToken = default
    )
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (!ReceivedMessages.TryGetValue(message.StorageId, out var current))
        {
            return ValueTask.FromResult(false);
        }

        bool updated;
        lock (current)
        {
            // Mirror the SQL providers' terminal guard (see ChangePublishStateAsync above).
            if (
                (current.StatusName is StatusName.Succeeded || current.StatusName is StatusName.Failed)
                && current.NextRetryAt is null
            )
            {
                return ValueTask.FromResult(false);
            }

            if (originalRetries.HasValue && current.Retries != originalRetries.Value)
            {
                return ValueTask.FromResult(false);
            }

            var utcNextRetryAt = nextRetryAt.ToUtcOrSelf();
            var utcLockedUntil = lockedUntil.ToUtcOrSelf();
            current.StatusName = state;
            current.ExpiresAt = message.ExpiresAt;
            current.NextRetryAt = utcNextRetryAt;
            current.LockedUntil = utcLockedUntil;
            current.Retries = message.Retries;
            current.Content = serializer.Serialize(message.Origin);
            current.ExceptionInfo = message.ExceptionInfo;
            updated = true;
        }

        return ValueTask.FromResult(updated);
    }

    public ValueTask<bool> LeaseReceiveAsync(
        MediumMessage message,
        DateTime lockedUntil,
        CancellationToken cancellationToken = default
    ) => _LeaseAsync(ReceivedMessages, message, lockedUntil, cancellationToken);

    public ValueTask<MediumMessage> StoreMessageAsync(
        string name,
        Message content,
        object? dbTransaction = null,
        CancellationToken cancellationToken = default
    )
    {
        cancellationToken.ThrowIfCancellationRequested();

        var added = timeProvider.GetUtcNow().UtcDateTime;
        var message = new MediumMessage
        {
            StorageId = longIdGenerator.Create(),
            Origin = content,
            Content = serializer.Serialize(content),
            Added = added,
            ExpiresAt = null,
            NextRetryAt = added.Add(messagingOptions.Value.RetryPolicy.InitialDispatchGrace),
            LockedUntil = null,
            Retries = 0,
        };

        PublishedMessages[message.StorageId] = new MemoryMessage
        {
            StorageId = message.StorageId,
            Name = name,
            Origin = message.Origin,
            Content = message.Content,
            Retries = message.Retries,
            Added = message.Added,
            ExpiresAt = message.ExpiresAt,
            NextRetryAt = message.NextRetryAt,
            LockedUntil = message.LockedUntil,
            StatusName = StatusName.Scheduled,
            Version = messagingOptions.Value.Version,
        };

        return ValueTask.FromResult(message);
    }

    public ValueTask<bool> StoreReceivedExceptionMessageAsync(
        string name,
        string group,
        string content,
        string? exceptionInfo = null,
        CancellationToken cancellationToken = default
    )
    {
        cancellationToken.ThrowIfCancellationRequested();

        var origin =
            serializer.Deserialize(content)
            ?? throw new InvalidOperationException("Failed to deserialize received exception message content.");

        var messageId = origin.GetId();
        var version = messagingOptions.Value.Version;
        var now = timeProvider.GetUtcNow().UtcDateTime;
        var expiresAt = now.AddSeconds(messagingOptions.Value.FailedMessageExpiredAfter);
        var retries = messagingOptions.Value.RetryPolicy.MaxPersistedRetries;
        var indexKey = (version, messageId, (string?)group);

        // Upsert on (Version, MessageId, Group) — mirrors the SQL providers' MERGE / ON CONFLICT
        // semantics so broker redelivery doesn't accumulate duplicate rows. The terminal-row guard
        // also matches: a Succeeded/Failed entry with no scheduled retry is left alone so a
        // previously-succeeded row isn't overwritten back to Failed by a redelivery-then-deserialize-fail.
        //
        // Lookup is O(1) via the secondary identity index, outside the per-write lock. The lock is
        // still required to serialize "decided not found → insert" against another concurrent
        // redelivery that made the same decision in the same micro-second — otherwise both would
        // race to insert duplicate rows. Lock scope is intentionally narrow: existence check inside
        // the lock so two losers fall through to the update branch.
        MemoryMessage? existing = null;
        if (
            _receivedIdentityIndex.TryGetValue(indexKey, out var existingId)
            && ReceivedMessages.TryGetValue(existingId, out var found)
        )
        {
            existing = found;
        }

        lock (_receivedUpsertLock)
        {
            // Re-check inside the lock so two concurrent inserts for the same identity converge to
            // a single row. The first arrival reserves the index slot below; the second observes it
            // here and takes the update branch.
            if (
                existing is null
                && _receivedIdentityIndex.TryGetValue(indexKey, out existingId)
                && ReceivedMessages.TryGetValue(existingId, out var foundUnderLock)
            )
            {
                existing = foundUnderLock;
            }

            if (existing is not null)
            {
                if (
                    (existing.StatusName is StatusName.Succeeded || existing.StatusName is StatusName.Failed)
                    && existing.NextRetryAt is null
                )
                {
                    // Terminal — leave it alone.
                    return ValueTask.FromResult(false);
                }

                existing.StatusName = StatusName.Failed;
                existing.Retries = retries;
                existing.ExpiresAt = expiresAt;
                existing.NextRetryAt = null;
                existing.LockedUntil = null;
                existing.Content = content;
                existing.ExceptionInfo = exceptionInfo;

                return ValueTask.FromResult(true);
            }

            var id = longIdGenerator.Create();
            ReceivedMessages[id] = new MemoryMessage
            {
                StorageId = id,
                Group = group,
                Origin = origin,
                Name = name,
                Content = content,
                Retries = retries,
                Added = now,
                ExpiresAt = expiresAt,
                NextRetryAt = null,
                LockedUntil = null,
                StatusName = StatusName.Failed,
                ExceptionInfo = exceptionInfo,
                Version = version,
            };
            _receivedIdentityIndex[indexKey] = id;

            return ValueTask.FromResult(true);
        }
    }

    public ValueTask<MediumMessage> StoreReceivedMessageAsync(
        string name,
        string group,
        Message message,
        CancellationToken cancellationToken = default
    )
    {
        cancellationToken.ThrowIfCancellationRequested();
        var version = messagingOptions.Value.Version;
        var added = timeProvider.GetUtcNow().UtcDateTime;
        var initialNextRetryAt = added.Add(messagingOptions.Value.RetryPolicy.InitialDispatchGrace);
        var serialized = serializer.Serialize(message);

        // Tolerate missing MessageId header (degenerate test inputs / synthetic payloads): without
        // a MessageId there's no upsert identity to share, so concurrent calls degrade to plain
        // inserts. This matches the SQL providers' MERGE/ON CONFLICT semantics — the constraint
        // is on MessageId, so a NULL MessageId effectively opts out of dedupe.
        var hasMessageId =
            message.Headers.TryGetValue(Headless.Messaging.Headers.MessageId, out var messageId)
            && messageId is not null;

        if (!hasMessageId)
        {
            var inserted = _InsertNewReceivedRow(name, group, message, serialized, added, initialNextRetryAt, version);
            return ValueTask.FromResult(inserted);
        }

        var indexKey = (version, messageId!, (string?)group);

        // R3 — extend the same lock + check-then-insert/update pattern from
        // StoreReceivedExceptionMessageAsync to the non-exception path. Before R3 two concurrent
        // StoreReceivedMessageAsync calls with the same (Version, MessageId, Group) tuple both
        // allocated distinct StorageIds, both wrote into ReceivedMessages, and both overwrote the
        // index slot last-writer-wins. _ClaimMessagesOfNeedRetry then returned BOTH rows, running
        // the consume executor twice. SQL providers enforce uniqueness via the DB constraint;
        // InMemory must enforce it explicitly under the same lock the exception path uses.
        MemoryMessage? existing = null;
        if (
            _receivedIdentityIndex.TryGetValue(indexKey, out var existingId)
            && ReceivedMessages.TryGetValue(existingId, out var found)
        )
        {
            existing = found;
        }

        lock (_receivedUpsertLock)
        {
            if (
                existing is null
                && _receivedIdentityIndex.TryGetValue(indexKey, out existingId)
                && ReceivedMessages.TryGetValue(existingId, out var foundUnderLock)
            )
            {
                existing = foundUnderLock;
            }

            if (existing is not null)
            {
                // Mirror the exception path's terminal-row guard: a Succeeded/Failed entry with no
                // scheduled retry is left alone so a redelivery cannot overwrite a previously-
                // terminal row. Surface the existing row as a snapshot to the caller so the
                // dispatcher continues to receive a MediumMessage value.
                if ((existing.StatusName is StatusName.Succeeded or StatusName.Failed) && existing.NextRetryAt is null)
                {
                    return ValueTask.FromResult(
                        new MediumMessage
                        {
                            StorageId = existing.StorageId,
                            Origin = existing.Origin,
                            Content = existing.Content,
                            Added = existing.Added,
                            ExpiresAt = existing.ExpiresAt,
                            NextRetryAt = existing.NextRetryAt,
                            LockedUntil = existing.LockedUntil,
                            Retries = existing.Retries,
                            ExceptionInfo = existing.ExceptionInfo,
                        }
                    );
                }

                // Non-terminal existing row: refresh in place with the latest payload + reset to
                // the freshly-stored Scheduled state, mirroring the SQL providers' MERGE WHEN
                // MATCHED UPDATE branch. Name/Group/Version are init-only on MemoryMessage; the
                // identity is keyed on (Version, MessageId, Group) so those values are pinned at
                // insert time and never need refreshing across redeliveries of the same identity.
                existing.Origin = message;
                existing.Content = serialized;
                existing.Retries = 0;
                existing.Added = added;
                existing.ExpiresAt = null;
                existing.NextRetryAt = initialNextRetryAt;
                existing.LockedUntil = null;
                existing.StatusName = StatusName.Scheduled;
                existing.ExceptionInfo = null;

                return ValueTask.FromResult(
                    new MediumMessage
                    {
                        StorageId = existing.StorageId,
                        Origin = existing.Origin,
                        Content = existing.Content,
                        Added = existing.Added,
                        ExpiresAt = existing.ExpiresAt,
                        NextRetryAt = existing.NextRetryAt,
                        LockedUntil = existing.LockedUntil,
                        Retries = existing.Retries,
                    }
                );
            }

            var inserted = _InsertNewReceivedRow(name, group, message, serialized, added, initialNextRetryAt, version);
            _receivedIdentityIndex[indexKey] = inserted.StorageId;
            return ValueTask.FromResult(inserted);
        }
    }

    private MediumMessage _InsertNewReceivedRow(
        string name,
        string group,
        Message message,
        string serialized,
        DateTime added,
        DateTime initialNextRetryAt,
        string version
    )
    {
        var mdMessage = new MediumMessage
        {
            StorageId = longIdGenerator.Create(),
            Origin = message,
            Content = serialized,
            Added = added,
            ExpiresAt = null,
            NextRetryAt = initialNextRetryAt,
            LockedUntil = null,
            Retries = 0,
        };

        ReceivedMessages[mdMessage.StorageId] = new MemoryMessage
        {
            StorageId = mdMessage.StorageId,
            Origin = mdMessage.Origin,
            Group = group,
            Name = name,
            Content = mdMessage.Content,
            Retries = mdMessage.Retries,
            Added = mdMessage.Added,
            ExpiresAt = mdMessage.ExpiresAt,
            NextRetryAt = mdMessage.NextRetryAt,
            LockedUntil = mdMessage.LockedUntil,
            StatusName = StatusName.Scheduled,
            Version = version,
        };

        return mdMessage;
    }

    public ValueTask<int> DeleteExpiresAsync(
        string table,
        DateTime timeout,
        int batchCount = 1000,
        CancellationToken cancellationToken = default
    )
    {
        cancellationToken.ThrowIfCancellationRequested();
        var removed = 0;
        if (string.Equals(table, nameof(PublishedMessages), StringComparison.Ordinal))
        {
            var ids = PublishedMessages
                .Values.Where(x =>
                    x.ExpiresAt < timeout && (x.StatusName == StatusName.Succeeded || x.StatusName == StatusName.Failed)
                )
                .Select(x => x.StorageId)
                .Take(batchCount);

            removed += ids.Count(id => PublishedMessages.TryRemove(id, out _));
        }
        else
        {
            var ids = ReceivedMessages
                .Values.Where(x =>
                    x.ExpiresAt < timeout && (x.StatusName == StatusName.Succeeded || x.StatusName == StatusName.Failed)
                )
                .Select(x => x.StorageId)
                .Take(batchCount)
                .ToList();

            foreach (var id in ids)
            {
                if (ReceivedMessages.TryRemove(id, out var removedMsg))
                {
                    _RemoveFromIdentityIndex(removedMsg);
                    removed++;
                }
            }
        }

        return ValueTask.FromResult(removed);
    }

    public ValueTask<IEnumerable<MediumMessage>> GetPublishedMessagesOfNeedRetryAsync(
        CancellationToken cancellationToken = default
    )
    {
        return ValueTask.FromResult(_ClaimMessagesOfNeedRetry(PublishedMessages, cancellationToken));
    }

    public ValueTask<IEnumerable<MediumMessage>> GetReceivedMessagesOfNeedRetryAsync(
        CancellationToken cancellationToken = default
    )
    {
        return ValueTask.FromResult(_ClaimMessagesOfNeedRetry(ReceivedMessages, cancellationToken));
    }

    private IEnumerable<MediumMessage> _ClaimMessagesOfNeedRetry(
        ConcurrentDictionary<long, MemoryMessage> source,
        CancellationToken cancellationToken
    )
    {
        cancellationToken.ThrowIfCancellationRequested();
        var now = timeProvider.GetUtcNow().UtcDateTime;
        var newLease = now.Add(messagingOptions.Value.RetryPolicy.DispatchTimeout);
        var maxPersistedRetries = messagingOptions.Value.RetryPolicy.MaxPersistedRetries;
        var version = messagingOptions.Value.Version;

        // Atomic claim-and-return mirrors the SQL providers' single-statement UPDATE...RETURNING/
        // OUTPUT semantics: the pickup query both leases (sets LockedUntil = now + DispatchTimeout)
        // and returns the rows in one step, preventing two concurrent pickups from observing the
        // same "LockedUntil IS NULL" row and double-dispatching. Each candidate row is leased under
        // its per-row lock so the claim is atomic with respect to other writers.
        //
        // Return a snapshot (plain MediumMessage), not the live MemoryMessage reference, so that
        // pre-write caller mutations (ExceptionInfo, ExpiresAt, AddOrUpdateException on Origin) do
        // NOT leak into the dictionary entry when ChangeReceiveStateAsync's terminal guard rejects
        // the conditional UPDATE. The SQL providers naturally produce a snapshot because every column
        // comes back through deserialization; InMemory must do this explicitly.
        var claimed = new List<MediumMessage>();
        foreach (var candidate in source.Values)
        {
            if (claimed.Count >= 200)
            {
                break;
            }

            if (!string.Equals(candidate.Version, version, StringComparison.Ordinal))
            {
                continue;
            }

            lock (candidate)
            {
                if (candidate.Retries > maxPersistedRetries)
                {
                    continue;
                }

                if (candidate.NextRetryAt is null || candidate.NextRetryAt > now)
                {
                    continue;
                }

                if (candidate.LockedUntil is not null && candidate.LockedUntil > now)
                {
                    continue;
                }

                // R7 — terminal-row exclusion is already enforced by the NextRetryAt > now check
                // above (terminal Succeeded/Failed rows have NextRetryAt IS NULL and so are
                // rejected by the `NextRetryAt is null` guard). The redundant terminal-status
                // block was unreachable and has been removed.
                candidate.LockedUntil = newLease;
                claimed.Add(_ToSnapshot(candidate));
            }
        }

        return claimed;
    }

    private static MediumMessage _ToSnapshot(MemoryMessage m) =>
        new()
        {
            StorageId = m.StorageId,
            // Clone the Origin's Headers dictionary so caller mutations (e.g., AddOrUpdateException
            // before a write that the terminal-row guard then rejects) cannot leak back into the
            // stored Origin. Value is shared by reference — payload semantics treat it as immutable.
            Origin = new Message(
                new Dictionary<string, string?>(m.Origin.Headers, StringComparer.Ordinal),
                m.Origin.Value
            ),
            Content = m.Content,
            Added = m.Added,
            ExpiresAt = m.ExpiresAt,
            NextRetryAt = m.NextRetryAt,
            LockedUntil = m.LockedUntil,
            Retries = m.Retries,
            ExceptionInfo = m.ExceptionInfo,
        };

    private static ValueTask<bool> _LeaseAsync(
        ConcurrentDictionary<long, MemoryMessage> messages,
        MediumMessage message,
        DateTime lockedUntil,
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
            if ((current.StatusName is StatusName.Succeeded or StatusName.Failed) && current.NextRetryAt is null)
            {
                return ValueTask.FromResult(false);
            }

            var utcLockedUntil = ((DateTime?)lockedUntil).ToUtcOrSelf();
            current.LockedUntil = utcLockedUntil;
            message.LockedUntil = utcLockedUntil;
            return ValueTask.FromResult(true);
        }
    }

    public ValueTask<int> DeleteReceivedMessageAsync(long id, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (ReceivedMessages.TryRemove(id, out var removed))
        {
            _RemoveFromIdentityIndex(removed);
            return ValueTask.FromResult(1);
        }
        return ValueTask.FromResult(0);
    }

    private void _RemoveFromIdentityIndex(MemoryMessage removed)
    {
        // Same tolerance as the insert path: only attempt the index removal when MessageId is
        // actually present on the row's headers. Rows that opted out of the upsert identity at
        // insert time are not in the secondary index — TryRemove on a synthesized key would be a
        // no-op anyway, but skipping the GetId() call avoids a KeyNotFoundException during
        // shutdown cleanup of degenerate test inputs.
        if (
            removed.Origin.Headers.TryGetValue(Headless.Messaging.Headers.MessageId, out var messageId)
            && messageId is not null
        )
        {
            _receivedIdentityIndex.TryRemove((removed.Version, messageId, removed.Group), out _);
        }
    }

    public ValueTask<int> DeletePublishedMessageAsync(long id, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var deleteResult = PublishedMessages.TryRemove(id, out _);
        return ValueTask.FromResult(deleteResult ? 1 : 0);
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

        // InMemory provider has no transaction handle; the nullability is part of the contract.
        return scheduleTask(null, result);
    }

    public IMonitoringApi GetMonitoringApi()
    {
        return new InMemoryMonitoringApi(this, timeProvider);
    }
}
