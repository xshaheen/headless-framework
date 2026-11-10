// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Collections.Concurrent;
using System.Data.Common;
using Headless.Coordination;
using Headless.Fencing;
using Headless.Messaging.Internal;
using Headless.Messaging.Persistence;
using Headless.Messaging.Transactions;
using Headless.UnitOfWork;
using Microsoft.Extensions.Options;

namespace Headless.Messaging.Storage.InMemory;

internal sealed partial class InMemoryDataStorage(
    IOptions<MessagingOptions> messagingOptions,
    IMessageSerializer serializer,
    IGuidGenerator guidGenerator,
    TimeProvider timeProvider,
    INodeMembership nodeMembership
)
    : IDataStorage,
        IDelayedMessageClaimStorage,
        IGracefulLeaseReleaseStorage,
        ICircuitRetryDeferralStorage,
        IDeliveryCoordinationResolver,
        ICoordinatedMessageStore
{
    public ConcurrentDictionary<Guid, MemoryMessage> PublishedMessages { get; } = new();

    public ConcurrentDictionary<Guid, MemoryMessage> ReceivedMessages { get; } = new();

    // Secondary index keyed on the SQL-providers' upsert identity (Version, MessageId, ConsumerIdentity, MessageLane).
    // Maps to the primary row id in <see cref="ReceivedMessages"/>. The lookup that backs
    // StoreReceivedExceptionMessageAsync is then O(1) via TryGetValue instead of an O(N) scan
    // over the whole received-message map. Updated in lockstep with every code path that inserts
    // into or removes from ReceivedMessages. ValueTuple's default equality uses ordinal string
    // equality for each component, matching the SQL providers' BINARY-collation key semantics.
    private readonly ConcurrentDictionary<
        (string Version, string MessageId, string ConsumerIdentity, MessageLane Lane),
        Guid
    > _receivedIdentityIndex = new();

    private readonly Dictionary<InboxKey, Guid> _inboxIdentityIndex = [];

    private readonly Dictionary<Guid, InMemoryOperationReceipt> _inboxOperationReceipts = [];

    private readonly List<InMemoryInboxAudit> _inboxAudit = [];

    // Serializes the lookup-then-insert/update paths in BOTH StoreReceivedExceptionMessageAsync
    // and StoreReceivedMessageAsync so two concurrent broker redeliveries (or two concurrent first
    // arrivals via the consume path) cannot both decide "not found" and race to insert duplicate
    // rows for the same (Version, MessageId, ConsumerIdentity) tuple. Renamed from _receivedExceptionUpsertLock
    // when the consume path adopted the same check-then-insert pattern.
    private readonly Lock _receivedUpsertLock = new();

    DeliveryCoordination IDeliveryCoordinationResolver.Resolve(IUnitOfWork unitOfWork)
    {
        // Rows are captured on the unit through ICoordinatedMessageStore and promoted only when it completes, which
        // comes after its commit, relational or not, and never after a rollback. That is as atomic as process-local
        // storage gets: a crash between the commit and the promotion loses the rows, as a crash loses every row this
        // storage holds. So any unit can host a publish, including one over a database transaction (an EF save).
        return DeliveryCoordination.Compatible(unitOfWork, transaction: null);
    }

    ValueTask<MediumMessage> ICoordinatedMessageStore.StoreCoordinatedMessageAsync(
        string name,
        MediumMessage message,
        DateTimeOffset? publishAt,
        IUnitOfWork unitOfWork,
        CancellationToken cancellationToken
    )
    {
        cancellationToken.ThrowIfCancellationRequested();

        // GetOrAdd runs before the row is built so the buffer's completion callback is registered before the
        // outbox buffer's dispatcher hand-off, which OutboxMessageWriter enlists only after this call returns.
        var buffer = unitOfWork.GetOrAdd(this, static (unit, storage) => new CoordinatedPublishBuffer(unit, storage));
        var (stored, row) = _CreateRow(name, message, publishAt);
        buffer.Add(row);

        return ValueTask.FromResult(stored);
    }

    /// <summary>
    /// Unit-local rows captured inside a unit of work. They join <see cref="PublishedMessages" />
    /// only when the unit completes; a failure disposes the buffer and the rows with it.
    /// </summary>
    // Same shape as MessageOutboxBuffer: rows wait in the scope-local buffer and are promoted on completion; a
    // failure never drains, so the buffered rows are simply dropped with the unit.
    private sealed class CoordinatedPublishBuffer : InMemoryWorkBuffer<MemoryMessage>
    {
        private readonly InMemoryDataStorage _storage;

        public CoordinatedPublishBuffer(IUnitOfWork unitOfWork, InMemoryDataStorage storage)
        {
            _storage = storage;
            unitOfWork.OnCompleted(_PromoteAsync);
        }

        private ValueTask _PromoteAsync()
        {
            foreach (var row in Drain())
            {
                // Stamped when the unit completes, so the marker is never earlier than the row becomes visible.
                _storage._StampChange(row);
                _storage.PublishedMessages[row.StorageId] = row;
            }

            return ValueTask.CompletedTask;
        }
    }

    public void Clear()
    {
        PublishedMessages.Clear();
        lock (_receivedUpsertLock)
        {
            ReceivedMessages.Clear();
            _receivedIdentityIndex.Clear();
            _inboxIdentityIndex.Clear();
            _inboxOperationReceipts.Clear();
            _inboxAudit.Clear();
        }
    }

    public ValueTask ChangePublishStateToDelayedAsync(
        IReadOnlyCollection<MediumMessage> messages,
        CancellationToken cancellationToken = default
    )
    {
        foreach (var expected in messages)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!PublishedMessages.TryGetValue(expected.StorageId, out var message))
            {
                continue;
            }

            lock (message)
            {
                if (
                    !_IsSupportedLane(message.Lane)
                    || message.Lane != expected.Lane
                    || MessageTerminalState.IsTerminal(message.StatusName, message.NextRetryAt)
                )
                {
                    continue;
                }

                // Mirrors the relational providers: a leased copy releases its own generation with the status flip, so
                // the row is re-claimable at once. An unleased hand-off copy flips only a row no attempt holds, still at
                // the generation the copy read, so it cannot reset a row another node has claimed and is sending since.
                if (expected.Lease.State == RowLeaseState.Active)
                {
                    var released = RowLeaseRules.Release(
                        message.Lease,
                        expected.Lease.Generation,
                        timeProvider.GetUtcNow()
                    );
                    if (released.Status != LeaseSettlementStatus.Released)
                    {
                        continue;
                    }

                    message.Lease = released.Next;
                }
                else if (
                    message.Lease.State == RowLeaseState.Active
                    || message.Lease.Generation != expected.Lease.Generation
                )
                {
                    continue;
                }

                message.StatusName = StatusName.Delayed;
                _StampChange(message);
            }
        }

        return ValueTask.CompletedTask;
    }

    public ValueTask<bool> ChangePublishStateAsync(
        MediumMessage message,
        StatusName state,
        MessageContentWrite contentWrite = MessageContentWrite.Preserve,
        DbTransaction? dbTransaction = null,
        RetryDelay? retryDelay = null,
        bool keepLease = false,
        int? originalRetries = null,
        CancellationToken cancellationToken = default
    )
    {
        return _ChangePublishStateAsync(
            message,
            state,
            contentWrite,
            retryDelay,
            keepLease,
            originalRetries,
            originalInlineAttempts: null,
            cancellationToken
        );
    }

    public ValueTask<bool> ChangePublishRetryStateAsync(
        MediumMessage message,
        StatusName state,
        MessageContentWrite contentWrite,
        RetryDelay? retryDelay,
        bool keepLease,
        int originalRetries,
        int originalInlineAttempts,
        CancellationToken cancellationToken = default
    )
    {
        return _ChangePublishStateAsync(
            message,
            state,
            contentWrite,
            retryDelay,
            keepLease,
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
            messagingOptions.Value.RetryPolicy.DispatchTimeout,
            timeProvider,
            cancellationToken
        );
    }

    public ValueTask<bool> RenewPublishLeaseAsync(
        MediumMessage message,
        TimeSpan leaseDuration,
        CancellationToken cancellationToken
    )
    {
        return _RenewLeaseAsync(PublishedMessages, message, leaseDuration, timeProvider, cancellationToken);
    }

    public ValueTask<bool> RenewReceiveLeaseAsync(
        MediumMessage message,
        TimeSpan leaseDuration,
        CancellationToken cancellationToken
    )
    {
        lock (_receivedUpsertLock)
        {
            return _RenewLeaseAsync(ReceivedMessages, message, leaseDuration, timeProvider, cancellationToken);
        }
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
        RetryDelay? retryDelay,
        bool keepLease,
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
            if (MessageTerminalState.IsTerminal(current.StatusName, current.NextRetryAt))
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
                !_TryEndLease(
                    current,
                    message,
                    state,
                    retryDelay,
                    keepLease,
                    fenced: originalInlineAttempts.HasValue,
                    out var lease
                )
            )
            {
                return ValueTask.FromResult(false);
            }

            var utcNextRetryAt = retryDelay?.ResolveDueAt(timeProvider.GetUtcNow(), current.NextRetryAt);
            current.StatusName = state;
            current.ExpiresAt = message.ExpiresAt;
            current.NextRetryAt = utcNextRetryAt;
            _ApplyLease(current, message, lease, fenced: originalInlineAttempts.HasValue);
            current.Retries = message.Retries;
            current.InlineAttempts = message.InlineAttempts;
            current.ExceptionInfo = message.ExceptionInfo;
            current.FailureFingerprint = message.FailureFingerprint;
            _WriteContent(current, message, contentWrite);
            _StampChange(current);
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
            nodeMembership.GetLeaseHolder(),
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
            nodeMembership.GetLeaseHolder(),
            fenceGenerator: null,
            cancellationToken: cancellationToken
        );
    }

    public ValueTask<bool> ChangeReceiveStateAsync(
        MediumMessage message,
        StatusName state,
        MessageContentWrite contentWrite = MessageContentWrite.Preserve,
        RetryDelay? retryDelay = null,
        bool keepLease = false,
        int? originalRetries = null,
        CancellationToken cancellationToken = default
    )
    {
        return _ChangeReceiveStateAsync(
            message,
            state,
            contentWrite,
            retryDelay,
            keepLease,
            originalRetries,
            originalInlineAttempts: null,
            cancellationToken
        );
    }

    public ValueTask<bool> ChangeReceiveRetryStateAsync(
        MediumMessage message,
        StatusName state,
        MessageContentWrite contentWrite,
        RetryDelay? retryDelay,
        bool keepLease,
        int originalRetries,
        int originalInlineAttempts,
        CancellationToken cancellationToken = default
    )
    {
        return _ChangeReceiveStateAsync(
            message,
            state,
            contentWrite,
            retryDelay,
            keepLease,
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
        lock (_receivedUpsertLock)
        {
            return _ReserveAttemptAsync(
                ReceivedMessages,
                message,
                originalInlineAttempts,
                messagingOptions.Value.RetryPolicy.DispatchTimeout,
                timeProvider,
                cancellationToken
            );
        }
    }

    private ValueTask<bool> _ChangeReceiveStateAsync(
        MediumMessage message,
        StatusName state,
        MessageContentWrite contentWrite,
        RetryDelay? retryDelay,
        bool keepLease,
        int? originalRetries,
        int? originalInlineAttempts,
        CancellationToken cancellationToken
    )
    {
        lock (_receivedUpsertLock)
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
                if (MessageTerminalState.IsTerminal(current.StatusName, current.NextRetryAt))
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

                if (current.InboxGeneration is not null && !_MatchesInboxFence(current, message.InboxAttemptFence))
                {
                    return ValueTask.FromResult(false);
                }

                if (
                    !_TryEndLease(
                        current,
                        message,
                        state,
                        retryDelay,
                        keepLease,
                        fenced: originalInlineAttempts.HasValue,
                        out var lease
                    )
                )
                {
                    return ValueTask.FromResult(false);
                }

                var utcNextRetryAt = retryDelay?.ResolveDueAt(timeProvider.GetUtcNow(), current.NextRetryAt);
                current.StatusName = state;
                current.ExpiresAt = message.ExpiresAt;
                current.NextRetryAt = utcNextRetryAt;
                _ApplyLease(current, message, lease, fenced: originalInlineAttempts.HasValue);
                current.Retries = message.Retries;
                current.InlineAttempts = message.InlineAttempts;
                current.InboxAttemptFence = keepLease ? current.InboxAttemptFence : null;
                if (
                    current.InboxGeneration is not null
                    && state is StatusName.Succeeded or StatusName.Failed
                    && utcNextRetryAt is null
                )
                {
                    var terminalAt = timeProvider.GetUtcNow();
                    current.TerminalAt = terminalAt;
                    current.EffectiveExpiresAt = terminalAt.Add(current.InboxRetention);
                }

                _WriteContent(current, message, contentWrite);
                current.ExceptionInfo = message.ExceptionInfo;
                current.FailureFingerprint = message.FailureFingerprint;
                _StampChange(current);
                updated = true;
            }

            return ValueTask.FromResult(updated);
        }
    }

    public ValueTask<bool> LeaseReceiveAsync(
        MediumMessage message,
        TimeSpan leaseDuration,
        CancellationToken cancellationToken = default
    )
    {
        lock (_receivedUpsertLock)
        {
            return _LeaseAsync(
                ReceivedMessages,
                message,
                leaseDuration,
                timeProvider,
                nodeMembership.GetLeaseHolder(),
                cancellationToken
            );
        }
    }

    public ValueTask<bool> LeaseReceiveAndReserveAttemptAsync(
        MediumMessage message,
        TimeSpan leaseDuration,
        int originalInlineAttempts,
        CancellationToken cancellationToken = default
    )
    {
        lock (_receivedUpsertLock)
        {
            return _LeaseAndReserveAttemptAsync(
                ReceivedMessages,
                message,
                leaseDuration,
                originalInlineAttempts,
                timeProvider,
                nodeMembership.GetLeaseHolder(),
                guidGenerator,
                cancellationToken
            );
        }
    }

    public ValueTask<InboxAdmissionResult> AdmitReceivedMessageAsync(
        string name,
        string consumerIdentity,
        string contractVersion,
        MediumMessage message,
        long generation = 0,
        TimeSpan? inboxRetention = null,
        CancellationToken cancellationToken = default
    )
    {
        cancellationToken.ThrowIfCancellationRequested();

        var tenantId = InboxAdmissionValidation.Validate(name, consumerIdentity, contractVersion, message, generation);
        var messageId = message.Origin.Id;
        var key = new InboxKey(tenantId, messageId, message.Lane, name, contractVersion, consumerIdentity, generation);
        var retention = inboxRetention ?? TimeSpan.FromDays(30);
        if (
            retention <= TimeSpan.Zero
            || retention.Ticks % TimeSpan.TicksPerSecond != 0
            || retention.TotalSeconds > int.MaxValue
        )
        {
            throw new ArgumentOutOfRangeException(
                nameof(inboxRetention),
                "Inbox retention must be a positive whole-second duration no greater than Int32.MaxValue seconds."
            );
        }

        lock (_receivedUpsertLock)
        {
            if (
                _inboxIdentityIndex.TryGetValue(key, out var existingId)
                && ReceivedMessages.TryGetValue(existingId, out var existing)
            )
            {
                lock (existing)
                {
                    var disposition = existing.StatusName switch
                    {
                        StatusName.Succeeded when existing.NextRetryAt is null =>
                            InboxAdmissionDisposition.SucceededDuplicate,
                        StatusName.Failed when existing.NextRetryAt is null =>
                            InboxAdmissionDisposition.TerminalFailedDuplicate,
                        _ => InboxAdmissionDisposition.InFlightDuplicate,
                    };

                    return ValueTask.FromResult(new InboxAdmissionResult(disposition, _ToSnapshot(existing)));
                }
            }

            _inboxIdentityIndex.Remove(key);

            var now = timeProvider.GetUtcNow();
            var incarnationId = guidGenerator.Create();
            var stored = new MemoryMessage
            {
                StorageId = guidGenerator.Create(),
                Origin = _CloneOrigin(message.Origin),
                Content = _ContentOf(message),
                Lane = message.Lane,
                Name = name,
                ConsumerIdentity = consumerIdentity,
                Version = messagingOptions.Value.Version,
                Added = now,
                NextRetryAt = now.Add(messagingOptions.Value.RetryPolicy.InitialDispatchGrace),
                StatusName = StatusName.Scheduled,
                InboxKey = key,
                InboxGeneration = new InboxGeneration(key.Generation, incarnationId),
                LifecycleId = incarnationId,
                InboxRetention = retention,
            };
            _StampChange(stored);

            ReceivedMessages[stored.StorageId] = stored;
            _inboxIdentityIndex[key] = stored.StorageId;

            return ValueTask.FromResult(
                new InboxAdmissionResult(InboxAdmissionDisposition.Winner, _ToSnapshot(stored))
            );
        }
    }

    public ValueTask<bool> IsInboxMessageAdmittedAsync(
        string? tenantId,
        string messageId,
        string messageName,
        string consumerIdentity,
        MessageLane lane,
        CancellationToken cancellationToken = default
    )
    {
        cancellationToken.ThrowIfCancellationRequested();

        // Every contract version and generation counts, replay children included, as on the relational providers: the
        // caller cannot know which one was admitted. The identity index holds root generations only, so the rows are
        // scanned without copying the dictionary; this runs only for a delivery that already failed before admission.
        foreach (var (_, row) in ReceivedMessages)
        {
            if (
                row.InboxKey is { } key
                && key.Lane == lane
                && string.Equals(key.MessageId, messageId, StringComparison.Ordinal)
                && string.Equals(key.ContractIdentity, messageName, StringComparison.Ordinal)
                && string.Equals(key.ConsumerIdentity, consumerIdentity, StringComparison.Ordinal)
                && string.Equals(key.TenantId, tenantId, StringComparison.Ordinal)
            )
            {
                return ValueTask.FromResult(true);
            }
        }

        return ValueTask.FromResult(false);
    }

    public ValueTask<bool> DeferReceivedInboxOrphanAsync(
        MediumMessage message,
        CancellationToken cancellationToken = default
    ) => _SetInboxRoutabilityAsync(message, orphaned: true, cancellationToken);

    public ValueTask<bool> ConfirmReceivedInboxRoutableAsync(
        MediumMessage message,
        CancellationToken cancellationToken = default
    ) => _SetInboxRoutabilityAsync(message, orphaned: false, cancellationToken);

    private ValueTask<bool> _SetInboxRoutabilityAsync(
        MediumMessage message,
        bool orphaned,
        CancellationToken cancellationToken
    )
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_receivedUpsertLock)
        {
            if (!ReceivedMessages.TryGetValue(message.StorageId, out var current))
            {
                return ValueTask.FromResult(false);
            }

            lock (current)
            {
                var now = timeProvider.GetUtcNow();
                if (
                    current.InboxKey is null
                    || !_MatchesInboxFence(current, message.InboxAttemptFence)
                    || RowLeaseRules.Judge(current.Lease, message.InboxAttemptFence!.LeaseGeneration, now)
                        != LeaseFenceStatus.Current
                )
                {
                    return ValueTask.FromResult(false);
                }

                if (current.IsInboxOrphaned != orphaned)
                {
                    _StampChange(current);
                }

                current.IsInboxOrphaned = orphaned;
                message.IsInboxOrphaned = orphaned;
                if (orphaned)
                {
                    // Orphaning releases the held generation with the probe delay; confirming keeps the live lease.
                    current.Lease = RowLeaseRules.Release(current.Lease, current.Lease.Generation, now).Next;
                    current.NextRetryAt = now.Add(messagingOptions.Value.OrphanProbeInterval);
                    message.NextRetryAt = current.NextRetryAt;
                }

                message.Lease = current.Lease;

                return ValueTask.FromResult(true);
            }
        }
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

        var (stored, row) = _CreateRow(name, message, publishAt);
        _StampChange(row);
        PublishedMessages[stored.StorageId] = row;

        return ValueTask.FromResult(stored);
    }

    private (MediumMessage Stored, MemoryMessage Row) _CreateRow(
        string name,
        MediumMessage message,
        DateTimeOffset? publishAt
    )
    {
        var added = timeProvider.GetUtcNow();
        var statusName =
            publishAt is null ? StatusName.Scheduled
            : publishAt <= added.AddMinutes(1) ? StatusName.Queued
            : StatusName.Delayed;
        var stored = new MediumMessage
        {
            StorageId = guidGenerator.Create(),
            Origin = message.Origin,
            Content = _ContentOf(message),
            Lane = message.Lane,
            Added = added,
            ExpiresAt = publishAt,
            NextRetryAt = publishAt is null ? added.Add(messagingOptions.Value.RetryPolicy.InitialDispatchGrace) : null,
            Retries = 0,
            InlineAttempts = 0,
        };

        var row = new MemoryMessage
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
            StatusName = statusName,
            Version = messagingOptions.Value.Version,
        };

        return (stored, row);
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
        string consumerIdentity,
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
            consumerIdentity,
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
        string consumerIdentity,
        MediumMessage message,
        string? exceptionInfo = null,
        CancellationToken cancellationToken = default
    )
    {
        cancellationToken.ThrowIfCancellationRequested();

        var content = _ContentOf(message);
        var messageId = message.Origin.Id;
        var version = messagingOptions.Value.Version;
        var now = timeProvider.GetUtcNow();
        var expiresAt = now.AddSeconds(messagingOptions.Value.FailedMessageExpiredAfter);
        // A poisoned row is terminal through NextRetryAt = null, not through its retry count; no consume attempt ran.
        const int retries = 0;
        var indexKey = (version, messageId, consumerIdentity, message.Lane);
        var failureFingerprint = message.FailureFingerprint ?? MessagingFailureFingerprints.Poison;

        // Upsert on (Version, MessageId, ConsumerIdentity) — mirrors the SQL providers' MERGE / ON CONFLICT
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
                if (MessageTerminalState.IsTerminal(existing.StatusName, existing.NextRetryAt))
                {
                    // Terminal — leave it alone.
                    return ValueTask.FromResult(false);
                }

                existing.StatusName = StatusName.Failed;
                existing.Retries = retries;
                existing.ExpiresAt = expiresAt;
                existing.NextRetryAt = null;
                existing.Lease = RowLeaseRules.Revoke(existing.Lease).Next;
                existing.Content = content;
                existing.ExceptionInfo = exceptionInfo;
                existing.FailureFingerprint = failureFingerprint;
                _StampChange(existing);

                return ValueTask.FromResult(true);
            }

            var id = guidGenerator.Create();
            var row = new MemoryMessage
            {
                StorageId = id,
                ConsumerIdentity = consumerIdentity,
                Origin = _CloneOrigin(message.Origin),
                Name = name,
                Content = content,
                Lane = message.Lane,
                Retries = retries,
                Added = now,
                ExpiresAt = expiresAt,
                NextRetryAt = null,
                StatusName = StatusName.Failed,
                ExceptionInfo = exceptionInfo,
                FailureFingerprint = failureFingerprint,
                Version = version,
            };
            _StampChange(row);
            ReceivedMessages[id] = row;
            _receivedIdentityIndex[indexKey] = id;

            return ValueTask.FromResult(true);
        }
    }

    public ValueTask<MediumMessage> StoreReceivedMessageAsync(
        string name,
        string consumerIdentity,
        MediumMessage message,
        CancellationToken cancellationToken = default
    )
    {
        cancellationToken.ThrowIfCancellationRequested();
        var version = messagingOptions.Value.Version;
        var added = timeProvider.GetUtcNow();
        var initialNextRetryAt = added.Add(messagingOptions.Value.RetryPolicy.InitialDispatchGrace);
        var origin = message.Origin;
        var serialized = _ContentOf(message);

        // Tolerate missing MessageId header (degenerate test inputs / synthetic payloads): without
        // a MessageId there's no upsert identity to share, so concurrent calls degrade to plain
        // inserts. This matches the SQL providers' MERGE/ON CONFLICT semantics — the constraint
        // is on MessageId, so a NULL MessageId effectively opts out of dedupe.
        var hasMessageId = origin.Headers.TryGetValue(Headers.MessageId, out var messageId) && messageId is not null;

        if (!hasMessageId)
        {
            var inserted = _InsertNewReceivedRow(
                name,
                consumerIdentity,
                message,
                serialized,
                added,
                initialNextRetryAt,
                version
            );
            return ValueTask.FromResult(inserted);
        }

        var indexKey = (version, messageId!, consumerIdentity, message.Lane);

        // Extend the same lock + check-then-insert/update pattern from
        // StoreReceivedExceptionMessageAsync to the non-exception path. Without it, two concurrent
        // StoreReceivedMessageAsync calls with the same (Version, MessageId, ConsumerIdentity) tuple both
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
                if (MessageTerminalState.IsTerminal(existing.StatusName, existing.NextRetryAt))
                {
                    return ValueTask.FromResult(
                        _CreateUnstoredReceivedMessage(message, serialized, added, initialNextRetryAt)
                    );
                }

                // Non-terminal, unleased existing row: refresh in place with the latest payload + reset to
                // the freshly-stored Scheduled state, mirroring the SQL providers' MERGE WHEN
                // MATCHED UPDATE branch. Name/ConsumerIdentity/Version are init-only on MemoryMessage; the
                // identity is keyed on (Version, MessageId, ConsumerIdentity) so those values are pinned at
                // insert time and never need refreshing across redeliveries of the same identity.
                //
                // #10 — gate the ENTIRE update under the live-lease check, matching the SQL
                // providers' `WHERE ... AND (LeaseState <> Active OR LeaseExpiresAt <= now())` clause
                // that suppresses the whole `ON CONFLICT DO UPDATE` while an attempt holds the row.
                // A redelivered message that arrives mid-dispatch must not mutate Retries (which
                // would silently rewind the counter), StatusName, Content, or any other column —
                // not just the lease. The post-fix-#7 Retries-CAS catches the Retries case, but
                // StatusName/Content/ExceptionInfo writes are not CAS-guarded and would corrupt
                // the row in subtle ways otherwise.
                var nowUtc = timeProvider.GetUtcNow();
                if (_HasLiveLease(existing, nowUtc))
                {
                    // Match SQL's guard-blocked upsert contract: return the fresh, unpersisted candidate.
                    // Its synthetic id makes the executor's follow-up lease fail, while its unleased state
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
                existing.Lease = RowLeaseRules.Revoke(existing.Lease).Next;
                existing.StatusName = StatusName.Scheduled;
                existing.ExceptionInfo = null;
                existing.FailureFingerprint = null;
                _StampChange(existing);

                return ValueTask.FromResult(_ToSnapshot(existing));
            }

            var inserted = _InsertNewReceivedRow(
                name,
                consumerIdentity,
                message,
                serialized,
                added,
                initialNextRetryAt,
                version
            );
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
            Retries = 0,
            InlineAttempts = 0,
        };
    }

    public ValueTask<MediumMessage> StoreReceivedMessageAsync(
        string name,
        string consumerIdentity,
        Message message,
        CancellationToken cancellationToken = default
    )
    {
        return StoreReceivedMessageAsync(
            name,
            consumerIdentity,
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
        string consumerIdentity,
        MediumMessage message,
        string serialized,
        DateTimeOffset added,
        DateTimeOffset initialNextRetryAt,
        string version
    )
    {
        var mdMessage = _CreateUnstoredReceivedMessage(message, serialized, added, initialNextRetryAt);

        var row = new MemoryMessage
        {
            StorageId = mdMessage.StorageId,
            Origin = _CloneOrigin(mdMessage.Origin),
            Lane = mdMessage.Lane,
            ConsumerIdentity = consumerIdentity,
            Name = name,
            Content = mdMessage.Content,
            Retries = mdMessage.Retries,
            InlineAttempts = mdMessage.InlineAttempts,
            Added = mdMessage.Added,
            ExpiresAt = mdMessage.ExpiresAt,
            NextRetryAt = mdMessage.NextRetryAt,
            StatusName = StatusName.Scheduled,
            Version = version,
        };
        _StampChange(row);
        ReceivedMessages[mdMessage.StorageId] = row;

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
            lock (_receivedUpsertLock)
            {
                var now = timeProvider.GetUtcNow();
                var rows = ReceivedMessages
                    .Values.Where(x =>
                        _IsSupportedLane(x.Lane)
                        && (x.InboxGeneration is null ? x.ExpiresAt < timeout : !x.IsHeld && x.EffectiveExpiresAt < now)
                        && x.NextRetryAt is null
                        && (x.StatusName == StatusName.Succeeded || x.StatusName == StatusName.Failed)
                    )
                    .Take(batchCount)
                    .ToList();

                foreach (var row in rows)
                {
                    if (row.InboxGeneration is { } generation)
                    {
                        var operationId = guidGenerator.Create();
                        const string actor = "headless.messaging.collector";
                        const string reason = "retention_expired";
                        var receipt = new InMemoryOperationReceipt(
                            operationId,
                            MessagingOperationTargetKind.Inbox,
                            MessagingOperationType.Cleanup,
                            InboxOperationOutcome.Applied,
                            actor,
                            reason,
                            now,
                            GenerationIncarnationId: generation.IncarnationId,
                            ExpectedStatus: row.StatusName,
                            StorageId: row.StorageId
                        );
                        _inboxOperationReceipts.Add(operationId, receipt);
                        _inboxAudit.Add(
                            new InMemoryInboxAudit(
                                guidGenerator.Create(),
                                operationId,
                                MessagingOperationTargetKind.Inbox,
                                generation.IncarnationId,
                                MessagingOperationType.Cleanup,
                                actor,
                                reason,
                                InboxOperationOutcome.Applied,
                                now
                            )
                        );
                    }

                    if (ReceivedMessages.TryRemove(row.StorageId, out var removedMsg))
                    {
                        _RemoveFromIdentityIndex(removedMsg);
                        if (removedMsg.InboxKey is { } key)
                        {
                            MessagingMetrics.RecordInbox(
                                InboxMetricKind.Retention,
                                key.ConsumerIdentity,
                                key.Lane,
                                InboxMetricOutcome.Expired,
                                InboxGuarantee.ProcessLocal,
                                "InMemory"
                            );
                        }

                        removed++;
                    }
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
            _ClaimMessagesOfNeedRetry(PublishedMessages, lane, cancellationToken: cancellationToken)
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
        IReadOnlyCollection<string>? consumerIdentities,
        CancellationToken cancellationToken = default
    )
    {
        lock (_receivedUpsertLock)
        {
            return ValueTask.FromResult<IEnumerable<MediumMessage>>(
                _ClaimMessagesOfNeedRetry(
                    ReceivedMessages,
                    lane,
                    orphaned: false,
                    consumerIdentities: consumerIdentities,
                    cancellationToken: cancellationToken
                )
            );
        }
    }

    public ValueTask<IEnumerable<MediumMessage>> GetReceivedInboxOrphansOfNeedRetryAsync(
        MessageLane lane,
        IReadOnlyCollection<string>? consumerIdentities,
        CancellationToken cancellationToken = default
    )
    {
        lock (_receivedUpsertLock)
        {
            return ValueTask.FromResult<IEnumerable<MediumMessage>>(
                _ClaimMessagesOfNeedRetry(
                    ReceivedMessages,
                    lane,
                    orphaned: true,
                    consumerIdentities: consumerIdentities,
                    cancellationToken: cancellationToken
                )
            );
        }
    }

    public ValueTask<int> ReclaimDeadReceivedOwnersAsync(
        IReadOnlyCollection<string> deadOwners,
        CancellationToken cancellationToken = default
    )
    {
        lock (_receivedUpsertLock)
        {
            return ValueTask.FromResult(_ReclaimDeadOwners(ReceivedMessages, deadOwners, cancellationToken));
        }
    }

    public ValueTask<bool> ReleasePublishedLeaseAsync(
        MessageLeaseIdentity identity,
        CancellationToken cancellationToken = default
    )
    {
        return ValueTask.FromResult(_ReleaseLease(PublishedMessages, identity, cancellationToken));
    }

    public ValueTask<bool> ReleaseReceivedLeaseAsync(
        MessageLeaseIdentity identity,
        CancellationToken cancellationToken = default
    )
    {
        lock (_receivedUpsertLock)
        {
            return ValueTask.FromResult(_ReleaseLease(ReceivedMessages, identity, cancellationToken));
        }
    }

    public ValueTask<bool> DeferReceivedRetryAsync(
        CircuitRetryDeferral deferral,
        CancellationToken cancellationToken = default
    )
    {
        lock (_receivedUpsertLock)
        {
            cancellationToken.ThrowIfCancellationRequested();
            _ = MessageLaneCompatibility.ToPersistedValue(deferral.Identity.Lane);

            if (!ReceivedMessages.TryGetValue(deferral.Identity.StorageId, out var current))
            {
                return ValueTask.FromResult(false);
            }

            lock (current)
            {
                // The deferral releases the held generation with the new due time, so the row waits out the circuit
                // instead of its lease.
                var now = timeProvider.GetUtcNow();
                if (!_TryReleaseLease(current, deferral.Identity, now))
                {
                    return ValueTask.FromResult(false);
                }

                current.NextRetryAt = now.Add(deferral.Delay);
                _StampChange(current);
                return ValueTask.FromResult(true);
            }
        }
    }

    public ValueTask<int> ReleasePublishedLeasesAsync(
        IReadOnlyCollection<MessageLeaseIdentity> identities,
        CancellationToken cancellationToken = default
    )
    {
        return ValueTask.FromResult(_ReleaseLeases(PublishedMessages, identities, cancellationToken));
    }

    public ValueTask<int> ReleaseReceivedLeasesAsync(
        IReadOnlyCollection<MessageLeaseIdentity> identities,
        CancellationToken cancellationToken = default
    )
    {
        lock (_receivedUpsertLock)
        {
            return ValueTask.FromResult(_ReleaseLeases(ReceivedMessages, identities, cancellationToken));
        }
    }

    private int _ReleaseLeases(
        ConcurrentDictionary<Guid, MemoryMessage> source,
        IReadOnlyCollection<MessageLeaseIdentity> identities,
        CancellationToken cancellationToken
    )
    {
        var released = 0;
        foreach (var identity in identities)
        {
            if (_ReleaseLease(source, identity, cancellationToken))
            {
                released++;
            }
        }

        return released;
    }

    private bool _ReleaseLease(
        ConcurrentDictionary<Guid, MemoryMessage> source,
        MessageLeaseIdentity identity,
        CancellationToken cancellationToken
    )
    {
        cancellationToken.ThrowIfCancellationRequested();
        _ = MessageLaneCompatibility.ToPersistedValue(identity.Lane);

        if (!source.TryGetValue(identity.StorageId, out var current))
        {
            return false;
        }

        lock (current)
        {
            return _TryReleaseLease(current, identity, timeProvider.GetUtcNow());
        }
    }

    /// <summary>
    /// Releases the lease generation <paramref name="identity" /> holds, so a release never ends a lease another node has
    /// taken since; the row must still be in the identity's lane, non-terminal, and on the identity's inbox attempt.
    /// </summary>
    private static bool _TryReleaseLease(MemoryMessage current, MessageLeaseIdentity identity, DateTimeOffset now)
    {
        if (
            current.Lane != identity.Lane
            || (current.InboxGeneration is not null && !_MatchesInboxFence(current, identity.InboxAttemptFence))
            || MessageTerminalState.IsTerminal(current.StatusName, current.NextRetryAt)
        )
        {
            return false;
        }

        // A generation already released reports Released without a write; like the relational release, which fences on
        // an active lease, only a release that ends the live generation counts.
        var released = RowLeaseRules.Release(current.Lease, identity.Generation, now);
        if (released.Status != LeaseSettlementStatus.Released || !released.Wrote)
        {
            return false;
        }

        current.Lease = released.Next;
        return true;
    }

    /// <summary>
    /// Judges and computes the lease a state change leaves, as the relational providers do. A fenced write (an inline
    /// retry that passed the attempt counter it read) applies only while the caller's generation is still current: it
    /// keeps the lease while the inline retry is in flight, settles it on a terminal outcome, and releases it on a retry
    /// schedule. An unfenced write rewrites the row's outcome, so it revokes whatever attempt holds the row unless it
    /// keeps the lease.
    /// </summary>
    private bool _TryEndLease(
        MemoryMessage current,
        MediumMessage message,
        StatusName state,
        RetryDelay? retryDelay,
        bool keepLease,
        bool fenced,
        out RowLeaseSnapshot lease
    )
    {
        if (!fenced)
        {
            lease = keepLease ? current.Lease : RowLeaseRules.Revoke(current.Lease).Next;
            return true;
        }

        var now = timeProvider.GetUtcNow();
        var generation = message.Lease.Generation;
        lease = current.Lease;

        if (keepLease)
        {
            return RowLeaseRules.Judge(current.Lease, generation, now) == LeaseFenceStatus.Current;
        }

        if (state is StatusName.Succeeded or StatusName.Failed && retryDelay is null)
        {
            var settled = RowLeaseRules.Settle(current.Lease, generation, now);
            lease = settled.Next;
            return settled.Status == LeaseSettlementStatus.Settled;
        }

        var released = RowLeaseRules.Release(current.Lease, generation, now);
        lease = released.Next;
        return released.Status == LeaseSettlementStatus.Released;
    }

    /// <summary>Stores the lease a state change left; a fenced write also hands it back on the caller's copy.</summary>
    private static void _ApplyLease(MemoryMessage current, MediumMessage message, RowLeaseSnapshot lease, bool fenced)
    {
        current.Lease = lease;
        if (fenced)
        {
            message.Lease = lease;
        }
    }

    private static bool _HasLiveLease(MediumMessage row, DateTimeOffset now)
    {
        return row.Lease.State == RowLeaseState.Active
            && RowLeaseRules.Judge(row.Lease, row.Lease.Generation, now) == LeaseFenceStatus.Current;
    }

    /// <summary>
    /// Grants a new lease generation to <paramref name="holder" /> unless a live attempt holds the row. A live lease the
    /// same holder already holds is refused too, as the relational claim skips every live row.
    /// </summary>
    private static bool _TryGrantLease(MemoryMessage current, DateTimeOffset now, TimeSpan duration, string? holder)
    {
        var granted = RowLeaseRules.Grant(current.Lease, now, duration, new LeaseGrantOptions { Holder = holder });
        if (!granted.Wrote)
        {
            return false;
        }

        current.Lease = granted.Next;
        return true;
    }

    private List<MediumMessage> _ClaimMessagesOfNeedRetry(
        ConcurrentDictionary<Guid, MemoryMessage> source,
        MessageLane lane,
        bool? orphaned = null,
        IReadOnlyCollection<string>? consumerIdentities = null,
        CancellationToken cancellationToken = default
    )
    {
        cancellationToken.ThrowIfCancellationRequested();
        _ = MessageLaneCompatibility.ToPersistedValue(lane);
        var now = timeProvider.GetUtcNow();
        var leaseDuration = messagingOptions.Value.RetryPolicy.DispatchTimeout;
        var holder = nodeMembership.GetLeaseHolder();
        var retryBatchSize = orphaned is true
            ? messagingOptions.Value.OrphanProbeBatchSize
            : messagingOptions.Value.RetryBatchSize;
        var version = messagingOptions.Value.Version;

        // Atomic claim-and-return mirrors the SQL providers' single-statement UPDATE...RETURNING/
        // OUTPUT semantics: the pickup query both leases (grants a new lease generation for DispatchTimeout)
        // and returns the rows in one step, preventing two concurrent pickups from observing the
        // same unleased row and double-dispatching. Each candidate row is leased under
        // its per-row lock so the claim is atomic with respect to other writers.
        //
        // Return a snapshot (plain MediumMessage), not the live MemoryMessage reference, so that
        // pre-write caller mutations (ExceptionInfo, ExpiresAt, AddOrUpdateException on Origin) do
        // NOT leak into the dictionary entry when ChangeReceiveStateAsync's terminal guard rejects
        // the conditional UPDATE. The SQL providers naturally produce a snapshot because every column
        // comes back through deserialization; InMemory must do this explicitly.
        //
        // Claim in NextRetryAt order, as the relational providers do: unordered enumeration lets an
        // earlier-scheduled row be starved indefinitely once the batch limit is reached. StorageId is
        // only a deterministic tie-break — it is not byte-comparable with uuid/uniqueidentifier
        // ordering, which matters to nothing, since no guarantee rests on rows sharing a NextRetryAt.
        // Rows with no NextRetryAt sort last and are rejected below without consuming a slot. Reading
        // the sort key outside the row lock is safe: eligibility is re-validated under it before leasing.
        var claimed = new List<MediumMessage>();
        var ordered = source
            .Values.OrderBy(candidate => candidate.NextRetryAt ?? DateTimeOffset.MaxValue)
            .ThenBy(candidate => candidate.StorageId);

        foreach (var candidate in ordered)
        {
            if (claimed.Count >= retryBatchSize)
            {
                break;
            }

            if (!string.Equals(candidate.Version, version, StringComparison.Ordinal))
            {
                continue;
            }

            // Skipped before the batch limit, as the relational providers filter inside their candidate query, so
            // rows of consumers this host does not run neither consume a batch slot nor get leased.
            if (consumerIdentities?.Contains(candidate.ConsumerIdentity, StringComparer.Ordinal) == false)
            {
                continue;
            }

            lock (candidate)
            {
                if (!_IsSupportedLane(candidate.Lane))
                {
                    continue;
                }

                if (orphaned.HasValue && candidate.IsInboxOrphaned != orphaned.Value)
                {
                    continue;
                }

                if (candidate.Lane != lane)
                {
                    continue;
                }

                if (candidate.NextRetryAt is null || candidate.NextRetryAt > now)
                {
                    continue;
                }

                // Terminal-row exclusion is already enforced by the NextRetryAt > now check
                // above (terminal Succeeded/Failed rows have NextRetryAt IS NULL and so are
                // rejected by the `NextRetryAt is null` guard). The redundant terminal-status
                // block was unreachable and has been removed.
                if (!_TryGrantLease(candidate, now, leaseDuration, holder))
                {
                    continue;
                }

                if (candidate.InboxGeneration is not null)
                {
                    candidate.InboxAttemptFence = new InboxAttemptFence(
                        candidate.StorageId,
                        candidate.Lane,
                        candidate.InboxGeneration.Number,
                        candidate.InboxGeneration.IncarnationId,
                        guidGenerator.Create(),
                        candidate.Lease.Generation
                    );
                }

                claimed.Add(_ToSnapshot(candidate));
            }
        }

        return claimed;
    }

    private static bool _IsSupportedLane(MessageLane lane)
    {
        return lane is MessageLane.Bus or MessageLane.Queue;
    }

    /// <summary>
    /// Copies an envelope crossing the store boundary in either direction. Caller mutations (e.g.
    /// <c>AddOrUpdateException</c> before a write the terminal-row guard then rejects) must not leak into the
    /// stored Origin, and a stored Origin must not drift under a caller that keeps editing its copy. The
    /// payload value is shared by reference — payload semantics treat it as immutable.
    /// </summary>
    // The content the caller already serialized for the origin when it supplied one, otherwise a fresh serialization.
    private string _ContentOf(MediumMessage message)
    {
        return string.IsNullOrEmpty(message.Content) ? serializer.Serialize(message.Origin) : message.Content;
    }

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
            Lease = m.Lease,
            Retries = m.Retries,
            InlineAttempts = m.InlineAttempts,
            InboxKey = m.InboxKey,
            InboxGeneration = m.InboxGeneration,
            InboxAttemptFence = m.InboxAttemptFence,
            IsInboxOrphaned = m.IsInboxOrphaned,
            ExceptionInfo = m.ExceptionInfo,
            FailureFingerprint = m.FailureFingerprint,
            Lane = m.Lane,
        };
    }

    private static bool _MatchesInboxFence(MemoryMessage current, InboxAttemptFence? fence)
    {
        return fence is not null
            && current.InboxAttemptFence == fence
            && fence.StorageId == current.StorageId
            && fence.Lane == current.Lane
            && current.InboxGeneration?.Number == fence.Generation
            && current.InboxGeneration?.IncarnationId == fence.GenerationIncarnationId
            && current.Lease.Generation == fence.LeaseGeneration;
    }

    private static ValueTask<bool> _LeaseAsync(
        ConcurrentDictionary<Guid, MemoryMessage> messages,
        MediumMessage message,
        TimeSpan leaseDuration,
        TimeProvider timeProvider,
        string? holder,
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
            if (MessageTerminalState.IsTerminal(current.StatusName, current.NextRetryAt))
            {
                return ValueTask.FromResult(false);
            }

            // #15 — explicit lease-contention guard: refuse to acquire the lease while a live attempt holds
            // the row, as the SQL providers' lease take skips live rows.
            if (!_TryGrantLease(current, timeProvider.GetUtcNow(), leaseDuration, holder))
            {
                return ValueTask.FromResult(false);
            }

            message.Lease = current.Lease;
            return ValueTask.FromResult(true);
        }
    }

    private static int _ReclaimDeadOwners(
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
        var deadHolders = new HashSet<string>(deadOwners, StringComparer.Ordinal);
        var reclaimed = 0;

        foreach (var message in messages.Values)
        {
            cancellationToken.ThrowIfCancellationRequested();

            lock (message)
            {
                if (
                    !_IsSupportedLane(message.Lane)
                    || message.Lease.Holder is not { } holder
                    || !deadHolders.Contains(holder)
                    || MessageTerminalState.IsTerminal(message.StatusName, message.NextRetryAt)
                )
                {
                    continue;
                }

                // Revokes the active attempt, live or expired, so the dead holder's generation is fenced out.
                var revoked = RowLeaseRules.Revoke(message.Lease);
                if (!revoked.Wrote)
                {
                    continue;
                }

                message.Lease = revoked.Next;
                reclaimed++;
            }
        }

        return reclaimed;
    }
}
