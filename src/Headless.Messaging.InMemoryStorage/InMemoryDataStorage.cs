// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Collections.Concurrent;
using Headless.Abstractions;
using Headless.Coordination;
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
    IGuidGenerator guidGenerator,
    TimeProvider timeProvider,
    INodeMembership nodeMembership
) : IDataStorage
{
    public ConcurrentDictionary<Guid, MemoryMessage> PublishedMessages { get; } = new();

    public ConcurrentDictionary<Guid, MemoryMessage> ReceivedMessages { get; } = new();

    // Secondary index keyed on the SQL-providers' upsert identity (Version, MessageId, Group?, IntentType).
    // Maps to the primary row id in <see cref="ReceivedMessages"/>. The lookup that backs
    // StoreReceivedExceptionMessageAsync is then O(1) via TryGetValue instead of an O(N) scan
    // over the whole received-message map. Updated in lockstep with every code path that inserts
    // into or removes from ReceivedMessages. ValueTuple's default equality uses ordinal string
    // equality for each component, matching the SQL providers' BINARY-collation key semantics.
    private readonly ConcurrentDictionary<
        (string Version, string MessageId, string? Group, IntentType IntentType),
        Guid
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
    }

    public ValueTask ChangePublishStateToDelayedAsync(Guid[] storageIds, CancellationToken cancellationToken = default)
    {
        foreach (var storageId in storageIds)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!PublishedMessages.TryGetValue(storageId, out var message))
            {
                continue;
            }

            lock (message)
            {
                if ((message.StatusName is StatusName.Succeeded or StatusName.Failed) && message.NextRetryAt is null)
                {
                    continue;
                }

                message.StatusName = StatusName.Delayed;
            }
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
            if ((current.StatusName is StatusName.Succeeded or StatusName.Failed) && current.NextRetryAt is null)
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
            current.Owner = utcLockedUntil is null ? null : nodeMembership.GetOwnerTag();
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
    ) =>
        _LeaseAsync(
            PublishedMessages,
            message,
            lockedUntil,
            timeProvider,
            nodeMembership.GetOwnerTag(),
            cancellationToken
        );

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
            if ((current.StatusName is StatusName.Succeeded or StatusName.Failed) && current.NextRetryAt is null)
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
            current.Owner = utcLockedUntil is null ? null : nodeMembership.GetOwnerTag();
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
    ) =>
        _LeaseAsync(
            ReceivedMessages,
            message,
            lockedUntil,
            timeProvider,
            nodeMembership.GetOwnerTag(),
            cancellationToken
        );

    public ValueTask<MediumMessage> StoreMessageAsync(
        string name,
        MediumMessage message,
        object? dbTransaction = null,
        CancellationToken cancellationToken = default
    )
    {
        cancellationToken.ThrowIfCancellationRequested();

        var added = timeProvider.GetUtcNow().UtcDateTime;
        var stored = new MediumMessage
        {
            StorageId = guidGenerator.Create(),
            Origin = message.Origin,
            Content = serializer.Serialize(message.Origin),
            IntentType = message.IntentType,
            Added = added,
            ExpiresAt = null,
            NextRetryAt = added.Add(messagingOptions.Value.RetryPolicy.InitialDispatchGrace),
            LockedUntil = null,
            Owner = null,
            Retries = 0,
        };

        PublishedMessages[stored.StorageId] = new MemoryMessage
        {
            StorageId = stored.StorageId,
            Name = name,
            Origin = stored.Origin,
            Content = stored.Content,
            IntentType = stored.IntentType,
            Retries = stored.Retries,
            Added = stored.Added,
            ExpiresAt = stored.ExpiresAt,
            NextRetryAt = stored.NextRetryAt,
            LockedUntil = stored.LockedUntil,
            Owner = stored.Owner,
            StatusName = StatusName.Scheduled,
            Version = messagingOptions.Value.Version,
        };

        return ValueTask.FromResult(stored);
    }

    public ValueTask<MediumMessage> StoreMessageAsync(
        string name,
        Message content,
        object? dbTransaction = null,
        CancellationToken cancellationToken = default
    ) =>
        StoreMessageAsync(
            name,
            new MediumMessage
            {
                StorageId = Guid.Empty,
                Origin = content,
                Content = string.Empty,
                IntentType = IntentType.Bus,
            },
            dbTransaction,
            cancellationToken
        );

    public ValueTask<bool> StoreReceivedExceptionMessageAsync(
        string name,
        string group,
        string content,
        string? exceptionInfo = null,
        CancellationToken cancellationToken = default
    )
    {
        var origin =
            serializer.Deserialize(content)
            ?? throw new InvalidOperationException("Failed to deserialize received exception message content.");

        return StoreReceivedExceptionMessageAsync(
            name,
            group,
            new MediumMessage
            {
                StorageId = Guid.Empty,
                Origin = origin,
                Content = content,
                IntentType = IntentType.Bus,
            },
            exceptionInfo,
            cancellationToken
        );
    }

    public ValueTask<bool> StoreReceivedExceptionMessageAsync(
        string name,
        string group,
        MediumMessage message,
        string? exceptionInfo = null,
        CancellationToken cancellationToken = default
    )
    {
        cancellationToken.ThrowIfCancellationRequested();

        var content = string.IsNullOrEmpty(message.Content) ? serializer.Serialize(message.Origin) : message.Content;
        var messageId = message.Origin.Id;
        var version = messagingOptions.Value.Version;
        var now = timeProvider.GetUtcNow().UtcDateTime;
        var expiresAt = now.AddSeconds(messagingOptions.Value.FailedMessageExpiredAfter);
        var retries = messagingOptions.Value.RetryPolicy.MaxPersistedRetries;
        var indexKey = (version, messageId, (string?)group, message.IntentType);

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
                if ((existing.StatusName is StatusName.Succeeded or StatusName.Failed) && existing.NextRetryAt is null)
                {
                    // Terminal — leave it alone.
                    return ValueTask.FromResult(false);
                }

                existing.StatusName = StatusName.Failed;
                existing.Retries = retries;
                existing.ExpiresAt = expiresAt;
                existing.NextRetryAt = null;
                existing.LockedUntil = null;
                existing.Owner = null;
                existing.Content = content;
                existing.ExceptionInfo = exceptionInfo;

                return ValueTask.FromResult(true);
            }

            var id = guidGenerator.Create();
            ReceivedMessages[id] = new MemoryMessage
            {
                StorageId = id,
                Group = group,
                Origin = message.Origin,
                Name = name,
                Content = content,
                IntentType = message.IntentType,
                Retries = retries,
                Added = now,
                ExpiresAt = expiresAt,
                NextRetryAt = null,
                LockedUntil = null,
                Owner = null,
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
        MediumMessage message,
        CancellationToken cancellationToken = default
    )
    {
        cancellationToken.ThrowIfCancellationRequested();
        var version = messagingOptions.Value.Version;
        var added = timeProvider.GetUtcNow().UtcDateTime;
        var initialNextRetryAt = added.Add(messagingOptions.Value.RetryPolicy.InitialDispatchGrace);
        var origin = message.Origin;
        var serialized = serializer.Serialize(origin);

        // Tolerate missing MessageId header (degenerate test inputs / synthetic payloads): without
        // a MessageId there's no upsert identity to share, so concurrent calls degrade to plain
        // inserts. This matches the SQL providers' MERGE/ON CONFLICT semantics — the constraint
        // is on MessageId, so a NULL MessageId effectively opts out of dedupe.
        var hasMessageId = origin.Headers.TryGetValue(Headers.MessageId, out var messageId) && messageId is not null;

        if (!hasMessageId)
        {
            var inserted = _InsertNewReceivedRow(name, group, message, serialized, added, initialNextRetryAt, version);
            return ValueTask.FromResult(inserted);
        }

        var indexKey = (version, messageId!, (string?)group, message.IntentType);

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
                // terminal row. Return a fresh unstored candidate whose synthetic id cannot lease
                // or execute that terminal row.
                if ((existing.StatusName is StatusName.Succeeded or StatusName.Failed) && existing.NextRetryAt is null)
                {
                    return ValueTask.FromResult(
                        _CreateUnstoredReceivedMessage(message, serialized, added, initialNextRetryAt)
                    );
                }

                // Non-terminal, unleased existing row: refresh in place with the latest payload + reset to
                // the freshly-stored Scheduled state, mirroring the SQL providers' MERGE WHEN
                // MATCHED UPDATE branch. Name/Group/Version are init-only on MemoryMessage; the
                // identity is keyed on (Version, MessageId, Group) so those values are pinned at
                // insert time and never need refreshing across redeliveries of the same identity.
                //
                // #10 — gate the ENTIRE update under the active-lease check, matching the SQL
                // providers' `WHERE ... AND (LockedUntil IS NULL OR LockedUntil <= now())` clause
                // that suppresses the whole `ON CONFLICT DO UPDATE` when the lease is active.
                // A redelivered message that arrives mid-dispatch must not mutate Retries (which
                // would silently rewind the counter), StatusName, Content, or any other column —
                // not just LockedUntil. The post-fix-#7 Retries-CAS catches the Retries case, but
                // StatusName/Content/ExceptionInfo writes are not CAS-guarded and would corrupt
                // the row in subtle ways otherwise.
                var nowUtc = timeProvider.GetUtcNow().UtcDateTime;
                var leaseActive = existing.LockedUntil is not null && existing.LockedUntil > nowUtc;
                if (leaseActive)
                {
                    // Match SQL's guard-blocked upsert contract: return the fresh, unpersisted candidate.
                    // Its synthetic id makes the executor's follow-up lease fail, while a null LockedUntil
                    // prevents the atomic-pickup fast path from treating another dispatcher's lease as ours.
                    return ValueTask.FromResult(
                        _CreateUnstoredReceivedMessage(message, serialized, added, initialNextRetryAt)
                    );
                }

                existing.Origin = message.Origin;
                existing.Content = serialized;
                existing.IntentType = message.IntentType;
                existing.Retries = 0;
                existing.Added = added;
                existing.ExpiresAt = null;
                existing.NextRetryAt = initialNextRetryAt;
                existing.LockedUntil = null;
                existing.Owner = null;
                existing.StatusName = StatusName.Scheduled;
                existing.ExceptionInfo = null;

                return ValueTask.FromResult(
                    _CreateUnstoredReceivedMessage(message, serialized, added, initialNextRetryAt, existing.StorageId)
                );
            }

            var inserted = _InsertNewReceivedRow(name, group, message, serialized, added, initialNextRetryAt, version);
            _receivedIdentityIndex[indexKey] = inserted.StorageId;
            return ValueTask.FromResult(inserted);
        }
    }

    private MediumMessage _CreateUnstoredReceivedMessage(
        MediumMessage message,
        string serialized,
        DateTime added,
        DateTime initialNextRetryAt,
        Guid? storageId = null
    ) =>
        new()
        {
            StorageId = storageId ?? guidGenerator.Create(),
            Origin = message.Origin,
            Content = serialized,
            IntentType = message.IntentType,
            Added = added,
            ExpiresAt = null,
            NextRetryAt = initialNextRetryAt,
            LockedUntil = null,
            Owner = null,
            Retries = 0,
        };

    public ValueTask<MediumMessage> StoreReceivedMessageAsync(
        string name,
        string group,
        Message message,
        CancellationToken cancellationToken = default
    ) =>
        StoreReceivedMessageAsync(
            name,
            group,
            new MediumMessage
            {
                StorageId = Guid.Empty,
                Origin = message,
                Content = string.Empty,
                IntentType = IntentType.Bus,
            },
            cancellationToken
        );

    private MediumMessage _InsertNewReceivedRow(
        string name,
        string group,
        MediumMessage message,
        string serialized,
        DateTime added,
        DateTime initialNextRetryAt,
        string version
    )
    {
        var mdMessage = _CreateUnstoredReceivedMessage(message, serialized, added, initialNextRetryAt);

        ReceivedMessages[mdMessage.StorageId] = new MemoryMessage
        {
            StorageId = mdMessage.StorageId,
            Origin = mdMessage.Origin,
            IntentType = mdMessage.IntentType,
            Group = group,
            Name = name,
            Content = mdMessage.Content,
            Retries = mdMessage.Retries,
            Added = mdMessage.Added,
            ExpiresAt = mdMessage.ExpiresAt,
            NextRetryAt = mdMessage.NextRetryAt,
            LockedUntil = mdMessage.LockedUntil,
            Owner = mdMessage.Owner,
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
                    x.ExpiresAt < timeout
                    && x.NextRetryAt is null
                    && (x.StatusName == StatusName.Succeeded || x.StatusName == StatusName.Failed)
                )
                .Select(x => x.StorageId)
                .Take(batchCount);

            removed += ids.Count(id => PublishedMessages.TryRemove(id, out _));
        }
        else
        {
            var ids = ReceivedMessages
                .Values.Where(x =>
                    x.ExpiresAt < timeout
                    && x.NextRetryAt is null
                    && (x.StatusName == StatusName.Succeeded || x.StatusName == StatusName.Failed)
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
        return ValueTask.FromResult<IEnumerable<MediumMessage>>(
            _ClaimMessagesOfNeedRetry(PublishedMessages, cancellationToken)
        );
    }

    public ValueTask<int> ReclaimDeadPublishedOwnersAsync(
        IReadOnlyCollection<string> deadOwners,
        CancellationToken cancellationToken = default
    )
    {
        return ValueTask.FromResult(_ReclaimDeadOwners(PublishedMessages, deadOwners, cancellationToken));
    }

    public ValueTask<IEnumerable<MediumMessage>> GetReceivedMessagesOfNeedRetryAsync(
        CancellationToken cancellationToken = default
    )
    {
        return ValueTask.FromResult<IEnumerable<MediumMessage>>(
            _ClaimMessagesOfNeedRetry(ReceivedMessages, cancellationToken)
        );
    }

    public ValueTask<int> ReclaimDeadReceivedOwnersAsync(
        IReadOnlyCollection<string> deadOwners,
        CancellationToken cancellationToken = default
    )
    {
        return ValueTask.FromResult(_ReclaimDeadOwners(ReceivedMessages, deadOwners, cancellationToken));
    }

    private List<MediumMessage> _ClaimMessagesOfNeedRetry(
        ConcurrentDictionary<Guid, MemoryMessage> source,
        CancellationToken cancellationToken
    )
    {
        cancellationToken.ThrowIfCancellationRequested();
        var now = timeProvider.GetUtcNow().UtcDateTime;
        var newLease = now.Add(messagingOptions.Value.RetryPolicy.DispatchTimeout);
        var maxPersistedRetries = messagingOptions.Value.RetryPolicy.MaxPersistedRetries;
        var retryBatchSize = messagingOptions.Value.RetryBatchSize;
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
            if (claimed.Count >= retryBatchSize)
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
                candidate.Owner = nodeMembership.GetOwnerTag();
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
            Owner = m.Owner,
            Retries = m.Retries,
            ExceptionInfo = m.ExceptionInfo,
            IntentType = m.IntentType,
        };

    private static ValueTask<bool> _LeaseAsync(
        ConcurrentDictionary<Guid, MemoryMessage> messages,
        MediumMessage message,
        DateTime lockedUntil,
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
            if ((current.StatusName is StatusName.Succeeded or StatusName.Failed) && current.NextRetryAt is null)
            {
                return ValueTask.FromResult(false);
            }

            // #15 — explicit lease-contention guard: refuse to acquire the lease when another writer
            // holds it (LockedUntil in the future). Mirrors the WHERE LockedUntil IS NULL OR <= @Now
            // predicate added to the SQL providers' _LeaseMessageAsync.
            var nowUtc = timeProvider.GetUtcNow().UtcDateTime;
            if (current.LockedUntil is not null && current.LockedUntil > nowUtc)
            {
                return ValueTask.FromResult(false);
            }

            var utcLockedUntil = ((DateTime?)lockedUntil).ToUtcOrSelf();
            current.LockedUntil = utcLockedUntil;
            current.Owner = owner;
            message.LockedUntil = utcLockedUntil;
            message.Owner = owner;
            return ValueTask.FromResult(true);
        }
    }

    private int _ReclaimDeadOwners(
        ConcurrentDictionary<Guid, MemoryMessage> messages,
        IReadOnlyCollection<string> deadOwners,
        CancellationToken cancellationToken
    )
    {
        cancellationToken.ThrowIfCancellationRequested();

        // Empty deadOwners trivially matches zero rows — short-circuit as an optimization (no row scan),
        // matching the PostgreSQL/SqlServer early returns and the IDataStorage no-op contract.
        if (deadOwners.Count == 0)
        {
            return 0;
        }

        // Always build an Ordinal HashSet so the owner comparison matches the PostgreSQL/SqlServer
        // exact-string semantics. The previous `as ISet<string>` fast path was both dead (the sole
        // caller passes a string[]) and a latent trap (a non-Ordinal ISet would silently diverge).
        var deadOwnerSet = new HashSet<string>(deadOwners, StringComparer.Ordinal);
        var now = timeProvider.GetUtcNow().UtcDateTime;
        var reclaimed = 0;

        foreach (var message in messages.Values)
        {
            cancellationToken.ThrowIfCancellationRequested();

            lock (message)
            {
                if (message.Owner is null || !deadOwnerSet.Contains(message.Owner))
                {
                    continue;
                }

                if ((message.StatusName is StatusName.Succeeded or StatusName.Failed) && message.NextRetryAt is null)
                {
                    continue;
                }

                if (message.LockedUntil is null || message.LockedUntil <= now)
                {
                    continue;
                }

                message.LockedUntil = now;
                reclaimed++;
            }
        }

        return reclaimed;
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
        // Same tolerance as the insert path: only attempt the index removal when MessageId is
        // actually present on the row's headers. Rows that opted out of the upsert identity at
        // insert time are not in the secondary index — TryRemove on a synthesized key would be a
        // no-op anyway, but skipping the GetId() call avoids a KeyNotFoundException during
        // shutdown cleanup of degenerate test inputs.
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

        // InMemory provider has no transaction handle; the nullability is part of the contract.
        return scheduleTask(null, result);
    }

    public IMonitoringApi GetMonitoringApi()
    {
        return new InMemoryMonitoringApi(this, timeProvider);
    }
}
