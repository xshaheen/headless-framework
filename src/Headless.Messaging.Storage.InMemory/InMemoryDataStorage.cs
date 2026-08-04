// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Collections.Concurrent;
using System.Data.Common;
using Headless.Abstractions;
using Headless.CommitCoordination;
using Headless.Coordination;
using Headless.Messaging.Configuration;
using Headless.Messaging.Internal;
using Headless.Messaging.Messages;
using Headless.Messaging.Monitoring;
using Headless.Messaging.Persistence;
using Headless.Messaging.Serialization;
using Microsoft.Extensions.Options;

namespace Headless.Messaging.Storage.InMemory;

internal sealed partial class InMemoryDataStorage(
    IOptions<MessagingOptions> messagingOptions,
    ISerializer serializer,
    IGuidGenerator guidGenerator,
    TimeProvider timeProvider,
    INodeMembership nodeMembership
) : IDataStorage, IDelayedMessageClaimStorage, IDeliveryCoordinationResolver
{
    public ConcurrentDictionary<Guid, MemoryMessage> PublishedMessages { get; } = new();

    public ConcurrentDictionary<Guid, MemoryMessage> ReceivedMessages { get; } = new();

    // Secondary index keyed on the SQL-providers' upsert identity (Version, MessageId, Group?, MessageLane).
    // Maps to the primary row id in <see cref="ReceivedMessages"/>. The lookup that backs
    // StoreReceivedExceptionMessageAsync is then O(1) via TryGetValue instead of an O(N) scan
    // over the whole received-message map. Updated in lockstep with every code path that inserts
    // into or removes from ReceivedMessages. ValueTuple's default equality uses ordinal string
    // equality for each component, matching the SQL providers' BINARY-collation key semantics.
    private readonly ConcurrentDictionary<
        (string Version, string MessageId, string? Group, MessageLane Lane),
        Guid
    > _receivedIdentityIndex = new();

    // Serializes the lookup-then-insert/update paths in BOTH StoreReceivedExceptionMessageAsync
    // and StoreReceivedMessageAsync so two concurrent broker redeliveries (or two concurrent first
    // arrivals via the consume path) cannot both decide "not found" and race to insert duplicate
    // rows for the same (Version, MessageId, Group) tuple. Renamed from _receivedExceptionUpsertLock
    // when the consume path adopted the same check-then-insert pattern in R3.
    private readonly Lock _receivedUpsertLock = new();

    DeliveryCoordination IDeliveryCoordinationResolver.Resolve(ICommitCoordinator coordinator)
    {
        if (coordinator.State is not CommitCoordinatorState.Active)
        {
            return DeliveryCoordination.Incompatible(DeliveryCoordinationMismatch.InactiveTransaction);
        }

        return coordinator.TryGetCapability<IRelationalCommitContext>(out _)
            ? DeliveryCoordination.Incompatible(DeliveryCoordinationMismatch.StorageProvider)
            : DeliveryCoordination.Incompatible(DeliveryCoordinationMismatch.MissingRelationalCapability);
    }

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
                if (
                    !_IsSupportedLane(message.Lane)
                    || (
                        (message.StatusName is StatusName.Succeeded or StatusName.Failed) && message.NextRetryAt is null
                    )
                )
                {
                    continue;
                }

                message.StatusName = StatusName.Delayed;

                // Release the ownership lease so the flushed-back row is immediately re-claimable on restart,
                // mirroring the relational providers. The graceful-shutdown flush owns these rows via its own claim.
                message.LockedUntil = null;
                message.Owner = null;
            }
        }

        return ValueTask.CompletedTask;
    }

    public ValueTask<bool> ChangePublishStateAsync(
        MediumMessage message,
        StatusName state,
        MessageContentWrite contentWrite = MessageContentWrite.Preserve,
        DbTransaction? dbTransaction = null,
        DateTimeOffset? nextRetryAt = null,
        DateTimeOffset? lockedUntil = null,
        int? originalRetries = null,
        CancellationToken cancellationToken = default
    )
    {
        return _ChangePublishStateAsync(
            message,
            state,
            contentWrite,
            nextRetryAt,
            lockedUntil,
            originalRetries,
            originalInlineAttempts: null,
            cancellationToken
        );
    }

    public ValueTask<bool> ChangePublishRetryStateAsync(
        MediumMessage message,
        StatusName state,
        MessageContentWrite contentWrite,
        DateTimeOffset? nextRetryAt,
        DateTimeOffset? lockedUntil,
        int originalRetries,
        int originalInlineAttempts,
        CancellationToken cancellationToken = default
    )
    {
        return _ChangePublishStateAsync(
            message,
            state,
            contentWrite,
            nextRetryAt,
            lockedUntil,
            originalRetries,
            originalInlineAttempts,
            cancellationToken
        );
    }

    public ValueTask<bool> ReservePublishAttemptAsync(
        MediumMessage message,
        int originalInlineAttempts,
        CancellationToken cancellationToken = default
    )
    {
        return _ReserveAttemptAsync(
            PublishedMessages,
            message,
            originalInlineAttempts,
            timeProvider,
            cancellationToken
        );
    }

    /// <summary>
    /// Applies the caller's envelope-write contract to a stored row. <see cref="MessageContentWrite.Preserve"/>
    /// leaves the stored envelope alone; <see cref="MessageContentWrite.Refresh"/> re-serializes the mutated
    /// origin and re-establishes the <c>Content == Serialize(Origin)</c> invariant on the caller's copy too.
    /// </summary>
    private void _WriteContent(MediumMessage stored, MediumMessage message, MessageContentWrite contentWrite)
    {
        if (contentWrite is not MessageContentWrite.Refresh)
        {
            return;
        }

        var content = serializer.Serialize(message.Origin);

        // Unlike the relational providers — whose row IS the serialized content — this provider keeps a
        // live Origin beside Content, so refreshing only Content would hand the next pickup an envelope
        // whose headers disagree with its bytes. Clone for the reason _ToSnapshot clones on the way out:
        // the caller goes on mutating its own copy after this write.
        stored.Origin = _CloneOrigin(message.Origin);
        stored.Content = content;
        message.Content = content;
    }

    private ValueTask<bool> _ChangePublishStateAsync(
        MediumMessage message,
        StatusName state,
        MessageContentWrite contentWrite,
        DateTimeOffset? nextRetryAt,
        DateTimeOffset? lockedUntil,
        int? originalRetries,
        int? originalInlineAttempts,
        CancellationToken cancellationToken
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

            if (originalInlineAttempts.HasValue && current.InlineAttempts != originalInlineAttempts.Value)
            {
                return ValueTask.FromResult(false);
            }

            if (
                originalInlineAttempts.HasValue
                && (
                    current.LockedUntil != message.LockedUntil
                    || !string.Equals(current.Owner, message.Owner, StringComparison.Ordinal)
                    || current.LockedUntil is null
                    || current.LockedUntil <= timeProvider.GetUtcNow()
                )
            )
            {
                return ValueTask.FromResult(false);
            }

            var utcNextRetryAt = nextRetryAt;
            var utcLockedUntil = lockedUntil;
            current.StatusName = state;
            current.ExpiresAt = message.ExpiresAt;
            current.NextRetryAt = utcNextRetryAt;
            current.LockedUntil = utcLockedUntil;
            current.Owner = utcLockedUntil is null ? null : nodeMembership.GetOwnerTag();
            current.Retries = message.Retries;
            current.InlineAttempts = message.InlineAttempts;
            _WriteContent(current, message, contentWrite);
            updated = true;
        }

        return ValueTask.FromResult(updated);
    }

    public ValueTask<bool> LeasePublishAsync(
        MediumMessage message,
        TimeSpan leaseDuration,
        CancellationToken cancellationToken = default
    )
    {
        return _LeaseAsync(
            PublishedMessages,
            message,
            leaseDuration,
            timeProvider,
            nodeMembership.GetOwnerTag(),
            cancellationToken
        );
    }

    public ValueTask<bool> LeasePublishAndReserveAttemptAsync(
        MediumMessage message,
        TimeSpan leaseDuration,
        int originalInlineAttempts,
        CancellationToken cancellationToken = default
    )
    {
        return _LeaseAndReserveAttemptAsync(
            PublishedMessages,
            message,
            leaseDuration,
            originalInlineAttempts,
            timeProvider,
            nodeMembership.GetOwnerTag(),
            cancellationToken
        );
    }

    public ValueTask<bool> ChangeReceiveStateAsync(
        MediumMessage message,
        StatusName state,
        MessageContentWrite contentWrite = MessageContentWrite.Preserve,
        DateTimeOffset? nextRetryAt = null,
        DateTimeOffset? lockedUntil = null,
        int? originalRetries = null,
        CancellationToken cancellationToken = default
    )
    {
        return _ChangeReceiveStateAsync(
            message,
            state,
            contentWrite,
            nextRetryAt,
            lockedUntil,
            originalRetries,
            originalInlineAttempts: null,
            cancellationToken
        );
    }

    public ValueTask<bool> ChangeReceiveRetryStateAsync(
        MediumMessage message,
        StatusName state,
        MessageContentWrite contentWrite,
        DateTimeOffset? nextRetryAt,
        DateTimeOffset? lockedUntil,
        int originalRetries,
        int originalInlineAttempts,
        CancellationToken cancellationToken = default
    )
    {
        return _ChangeReceiveStateAsync(
            message,
            state,
            contentWrite,
            nextRetryAt,
            lockedUntil,
            originalRetries,
            originalInlineAttempts,
            cancellationToken
        );
    }

    public ValueTask<bool> ReserveReceiveAttemptAsync(
        MediumMessage message,
        int originalInlineAttempts,
        CancellationToken cancellationToken = default
    )
    {
        return _ReserveAttemptAsync(ReceivedMessages, message, originalInlineAttempts, timeProvider, cancellationToken);
    }

    private ValueTask<bool> _ChangeReceiveStateAsync(
        MediumMessage message,
        StatusName state,
        MessageContentWrite contentWrite,
        DateTimeOffset? nextRetryAt,
        DateTimeOffset? lockedUntil,
        int? originalRetries,
        int? originalInlineAttempts,
        CancellationToken cancellationToken
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

            if (originalInlineAttempts.HasValue && current.InlineAttempts != originalInlineAttempts.Value)
            {
                return ValueTask.FromResult(false);
            }

            if (
                originalInlineAttempts.HasValue
                && (
                    current.LockedUntil != message.LockedUntil
                    || !string.Equals(current.Owner, message.Owner, StringComparison.Ordinal)
                    || current.LockedUntil is null
                    || current.LockedUntil <= timeProvider.GetUtcNow()
                )
            )
            {
                return ValueTask.FromResult(false);
            }

            var utcNextRetryAt = nextRetryAt;
            var utcLockedUntil = lockedUntil;
            current.StatusName = state;
            current.ExpiresAt = message.ExpiresAt;
            current.NextRetryAt = utcNextRetryAt;
            current.LockedUntil = utcLockedUntil;
            current.Owner = utcLockedUntil is null ? null : nodeMembership.GetOwnerTag();
            current.Retries = message.Retries;
            current.InlineAttempts = message.InlineAttempts;
            _WriteContent(current, message, contentWrite);
            current.ExceptionInfo = message.ExceptionInfo;
            updated = true;
        }

        return ValueTask.FromResult(updated);
    }

    public ValueTask<bool> LeaseReceiveAsync(
        MediumMessage message,
        TimeSpan leaseDuration,
        CancellationToken cancellationToken = default
    )
    {
        return _LeaseAsync(
            ReceivedMessages,
            message,
            leaseDuration,
            timeProvider,
            nodeMembership.GetOwnerTag(),
            cancellationToken
        );
    }

    public ValueTask<bool> LeaseReceiveAndReserveAttemptAsync(
        MediumMessage message,
        TimeSpan leaseDuration,
        int originalInlineAttempts,
        CancellationToken cancellationToken = default
    )
    {
        return _LeaseAndReserveAttemptAsync(
            ReceivedMessages,
            message,
            leaseDuration,
            originalInlineAttempts,
            timeProvider,
            nodeMembership.GetOwnerTag(),
            cancellationToken
        );
    }

    public ValueTask<MediumMessage> StoreMessageAsync(
        string name,
        MediumMessage message,
        DbTransaction? dbTransaction = null,
        CancellationToken cancellationToken = default
    )
    {
        return _StoreMessageAsync(name, message, publishAt: null, cancellationToken);
    }

    public ValueTask<MediumMessage> StoreScheduledMessageAsync(
        string name,
        MediumMessage message,
        DateTimeOffset publishAt,
        DbTransaction? transaction = null,
        CancellationToken cancellationToken = default
    )
    {
        return _StoreMessageAsync(name, message, publishAt, cancellationToken);
    }

    private ValueTask<MediumMessage> _StoreMessageAsync(
        string name,
        MediumMessage message,
        DateTimeOffset? publishAt,
        CancellationToken cancellationToken
    )
    {
        cancellationToken.ThrowIfCancellationRequested();

        var added = timeProvider.GetUtcNow();
        var statusName =
            publishAt is null ? StatusName.Scheduled
            : publishAt <= added.AddMinutes(1) ? StatusName.Queued
            : StatusName.Delayed;
        var stored = new MediumMessage
        {
            StorageId = guidGenerator.Create(),
            Origin = message.Origin,
            Content = serializer.Serialize(message.Origin),
            Lane = message.Lane,
            Added = added,
            ExpiresAt = publishAt,
            NextRetryAt = publishAt is null ? added.Add(messagingOptions.Value.RetryPolicy.InitialDispatchGrace) : null,
            LockedUntil = null,
            Owner = null,
            Retries = 0,
            InlineAttempts = 0,
        };

        PublishedMessages[stored.StorageId] = new MemoryMessage
        {
            StorageId = stored.StorageId,
            Name = name,
            Origin = _CloneOrigin(stored.Origin),
            Content = stored.Content,
            Lane = stored.Lane,
            Retries = stored.Retries,
            InlineAttempts = stored.InlineAttempts,
            Added = stored.Added,
            ExpiresAt = stored.ExpiresAt,
            NextRetryAt = stored.NextRetryAt,
            LockedUntil = stored.LockedUntil,
            Owner = stored.Owner,
            StatusName = statusName,
            Version = messagingOptions.Value.Version,
        };

        return ValueTask.FromResult(stored);
    }

    public ValueTask<MediumMessage> StoreMessageAsync(
        string name,
        Message content,
        DbTransaction? dbTransaction = null,
        CancellationToken cancellationToken = default
    )
    {
        return StoreMessageAsync(
            name,
            new MediumMessage
            {
                StorageId = Guid.Empty,
                Origin = content,
                Content = string.Empty,
                Lane = MessageLane.Bus,
            },
            dbTransaction,
            cancellationToken
        );
    }

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
                Lane = MessageLane.Bus,
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
        var now = timeProvider.GetUtcNow();
        var expiresAt = now.AddSeconds(messagingOptions.Value.FailedMessageExpiredAfter);
        var retries = messagingOptions.Value.RetryPolicy.MaxPersistedRetries;
        var indexKey = (version, messageId, (string?)group, message.Lane);

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
                Origin = _CloneOrigin(message.Origin),
                Name = name,
                Content = content,
                Lane = message.Lane,
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
        var added = timeProvider.GetUtcNow();
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

        var indexKey = (version, messageId!, (string?)group, message.Lane);

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
                var nowUtc = timeProvider.GetUtcNow();
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

                existing.Origin = _CloneOrigin(message.Origin);
                existing.Content = serialized;
                existing.Lane = message.Lane;
                // Redelivery refreshes the envelope but cannot replenish durable retry budgets.
                // The existing counters remain authoritative across lease expiry and restart.
                existing.Added = added;
                existing.ExpiresAt = null;
                existing.NextRetryAt = initialNextRetryAt;
                existing.LockedUntil = null;
                existing.Owner = null;
                existing.StatusName = StatusName.Scheduled;
                existing.ExceptionInfo = null;

                return ValueTask.FromResult(_ToSnapshot(existing));
            }

            var inserted = _InsertNewReceivedRow(name, group, message, serialized, added, initialNextRetryAt, version);
            _receivedIdentityIndex[indexKey] = inserted.StorageId;
            return ValueTask.FromResult(inserted);
        }
    }

    private MediumMessage _CreateUnstoredReceivedMessage(
        MediumMessage message,
        string serialized,
        DateTimeOffset added,
        DateTimeOffset initialNextRetryAt,
        Guid? storageId = null
    )
    {
        return new()
        {
            StorageId = storageId ?? guidGenerator.Create(),
            Origin = message.Origin,
            Content = serialized,
            Lane = message.Lane,
            Added = added,
            ExpiresAt = null,
            NextRetryAt = initialNextRetryAt,
            LockedUntil = null,
            Owner = null,
            Retries = 0,
            InlineAttempts = 0,
        };
    }

    public ValueTask<MediumMessage> StoreReceivedMessageAsync(
        string name,
        string group,
        Message message,
        CancellationToken cancellationToken = default
    )
    {
        return StoreReceivedMessageAsync(
            name,
            group,
            new MediumMessage
            {
                StorageId = Guid.Empty,
                Origin = message,
                Content = string.Empty,
                Lane = MessageLane.Bus,
            },
            cancellationToken
        );
    }

    private MediumMessage _InsertNewReceivedRow(
        string name,
        string group,
        MediumMessage message,
        string serialized,
        DateTimeOffset added,
        DateTimeOffset initialNextRetryAt,
        string version
    )
    {
        var mdMessage = _CreateUnstoredReceivedMessage(message, serialized, added, initialNextRetryAt);

        ReceivedMessages[mdMessage.StorageId] = new MemoryMessage
        {
            StorageId = mdMessage.StorageId,
            Origin = _CloneOrigin(mdMessage.Origin),
            Lane = mdMessage.Lane,
            Group = group,
            Name = name,
            Content = mdMessage.Content,
            Retries = mdMessage.Retries,
            InlineAttempts = mdMessage.InlineAttempts,
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
        DateTimeOffset timeout,
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
                    _IsSupportedLane(x.Lane)
                    && x.ExpiresAt < timeout
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
                    _IsSupportedLane(x.Lane)
                    && x.ExpiresAt < timeout
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
        MessageLane lane,
        CancellationToken cancellationToken = default
    )
    {
        return ValueTask.FromResult<IEnumerable<MediumMessage>>(
            _ClaimMessagesOfNeedRetry(PublishedMessages, lane, cancellationToken)
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
        MessageLane lane,
        CancellationToken cancellationToken = default
    )
    {
        return ValueTask.FromResult<IEnumerable<MediumMessage>>(
            _ClaimMessagesOfNeedRetry(ReceivedMessages, lane, cancellationToken)
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
        MessageLane lane,
        CancellationToken cancellationToken
    )
    {
        cancellationToken.ThrowIfCancellationRequested();
        _ = MessageLaneCompatibility.ToPersistedValue(lane);
        var now = timeProvider.GetUtcNow();
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
                if (!_IsSupportedLane(candidate.Lane))
                {
                    continue;
                }

                if (candidate.Lane != lane)
                {
                    continue;
                }

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

    private static bool _IsSupportedLane(MessageLane lane)
    {
        return lane is MessageLane.Bus or MessageLane.Queue;
    }

    private static bool _IsEligibleRetryCandidate(MemoryMessage candidate, DateTimeOffset now, int maxPersistedRetries)
    {
        if ((candidate.StatusName is StatusName.Succeeded or StatusName.Failed) && candidate.NextRetryAt is null)
        {
            return false;
        }

        return candidate.Retries <= maxPersistedRetries
            && candidate.NextRetryAt is not null
            && candidate.NextRetryAt <= now
            && (candidate.LockedUntil is null || candidate.LockedUntil <= now);
    }

    /// <summary>
    /// Copies an envelope crossing the store boundary in either direction. Caller mutations (e.g.
    /// <c>AddOrUpdateException</c> before a write the terminal-row guard then rejects) must not leak into the
    /// stored Origin, and a stored Origin must not drift under a caller that keeps editing its copy. The
    /// payload value is shared by reference — payload semantics treat it as immutable.
    /// </summary>
    private static Message _CloneOrigin(Message origin)
    {
        return new Message(new Dictionary<string, string?>(origin.Headers, StringComparer.Ordinal), origin.Value);
    }

    private static MediumMessage _ToSnapshot(MemoryMessage m)
    {
        return new()
        {
            StorageId = m.StorageId,
            Origin = _CloneOrigin(m.Origin),
            Content = m.Content,
            Added = m.Added,
            ExpiresAt = m.ExpiresAt,
            NextRetryAt = m.NextRetryAt,
            LockedUntil = m.LockedUntil,
            Owner = m.Owner,
            Retries = m.Retries,
            InlineAttempts = m.InlineAttempts,
            ExceptionInfo = m.ExceptionInfo,
            Lane = m.Lane,
        };
    }

    private static ValueTask<bool> _LeaseAsync(
        ConcurrentDictionary<Guid, MemoryMessage> messages,
        MediumMessage message,
        TimeSpan leaseDuration,
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
            var nowUtc = timeProvider.GetUtcNow();
            if (current.LockedUntil is not null && current.LockedUntil > nowUtc)
            {
                return ValueTask.FromResult(false);
            }

            var lockedUntil = nowUtc.Add(leaseDuration);
            current.LockedUntil = lockedUntil;
            current.Owner = owner;
            message.LockedUntil = lockedUntil;
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
        var now = timeProvider.GetUtcNow();
        var reclaimed = 0;

        foreach (var message in messages.Values)
        {
            cancellationToken.ThrowIfCancellationRequested();

            lock (message)
            {
                if (!_IsSupportedLane(message.Lane) || message.Owner is null || !deadOwnerSet.Contains(message.Owner))
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
}
