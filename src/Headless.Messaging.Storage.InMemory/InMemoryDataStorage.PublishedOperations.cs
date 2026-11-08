// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;
using Headless.Messaging.Internal;
using Headless.Messaging.Persistence;

namespace Headless.Messaging.Storage.InMemory;

// Audited re-send and delete of published rows, single-row and as Operations over a frozen scope. Each write and its
// receipt happen under the store lock the ledger read takes. A row an additional outbox holds is changed there, and
// only its receipt is recorded here.
internal sealed partial class InMemoryDataStorage : IAdditionalOutboxOperations
{
    private sealed class InMemoryPublishedOperationsApi(InMemoryDataStorage storage) : IPublishedOperationsApi
    {
        public ValueTask<MessageCount> CountOperationScopeAsync(
            MessageOperationScope scope,
            int cap = MessageCount.DefaultCap,
            CancellationToken cancellationToken = default
        ) => storage._CountPublishedScopeAsync(scope, cap, cancellationToken);

        public ValueTask<MessageOperationChunk> ReadOperationScopeAsync(
            MessageOperationScope scope,
            Guid? afterId,
            int chunkSize,
            CancellationToken cancellationToken = default
        ) => storage._ReadPublishedScopeAsync(scope, afterId, chunkSize, cancellationToken);

        public ValueTask<PublishedOperationResult> ResendAsync(
            PublishedOperationRequest request,
            CancellationToken cancellationToken = default
        ) => storage._MutatePublishedAsync(MessagingOperationType.Resend, request, cancellationToken);

        public ValueTask<PublishedOperationResult> PurgeAsync(
            PublishedOperationRequest request,
            CancellationToken cancellationToken = default
        ) => storage._MutatePublishedAsync(MessagingOperationType.Purge, request, cancellationToken);
    }

    public IPublishedOperationsApi GetPublishedOperationsApi() => new InMemoryPublishedOperationsApi(this);

    private ValueTask<MessageCount> _CountPublishedScopeAsync(
        MessageOperationScope scope,
        int cap,
        CancellationToken cancellationToken
    )
    {
        cancellationToken.ThrowIfCancellationRequested();
        Argument.IsPositive(cap);
        var inScope = _PublishedScopePredicate(scope);

        return ValueTask.FromResult(_CountScope(PublishedMessages, inScope, cap));
    }

    private ValueTask<MessageOperationChunk> _ReadPublishedScopeAsync(
        MessageOperationScope scope,
        Guid? afterId,
        int chunkSize,
        CancellationToken cancellationToken
    )
    {
        cancellationToken.ThrowIfCancellationRequested();
        Argument.IsInclusiveBetween(chunkSize, 1, MessageOperationChunk.MaxChunkSize);
        var inScope = _PublishedScopePredicate(scope);

        return ValueTask.FromResult(
            _ReadScopeChunk(PublishedMessages, inScope, afterId, chunkSize, static _ => Guid.Empty)
        );
    }

    private static Func<MemoryMessage, bool> _PublishedScopePredicate(MessageOperationScope scope)
    {
        Argument.IsNotNull(scope);
        Argument.IsNotNull(scope.Query);

        if (scope.Query.MessageType != MessageType.Publish)
        {
            throw new ArgumentException("A published Operation scope reads published rows only.", nameof(scope));
        }

        Func<MemoryMessage, bool> eligible = scope.Kind switch
        {
            MessageOperationKind.Resend => static message => message.StatusName == StatusName.Failed,
            MessageOperationKind.Delete => static _ => true,
            _ => throw new ArgumentOutOfRangeException(
                nameof(scope),
                scope.Kind,
                "A published Operation re-sends or deletes."
            ),
        };

        return message =>
            InMemoryMessageFilter.Matches(message, scope.Query)
            && MessageTerminalState.IsTerminal(message.StatusName, message.NextRetryAt)
            && message.ChangedAt <= scope.HighWaterMark
            && eligible(message);
    }

    private ValueTask<PublishedOperationResult> _MutatePublishedAsync(
        MessagingOperationType operationType,
        PublishedOperationRequest request,
        CancellationToken cancellationToken
    )
    {
        cancellationToken.ThrowIfCancellationRequested();
        Argument.IsNotNull(request);
        request.Validate();

        lock (_receivedUpsertLock)
        {
            var attempt = new InMemoryOperationReceipt(
                request.OperationId,
                MessagingOperationTargetKind.Published,
                operationType,
                default,
                request.Actor,
                request.StoredReason,
                default,
                StorageId: request.StorageId
            );
            if (_inboxOperationReceipts.TryGetValue(request.OperationId, out var prior))
            {
                return ValueTask.FromResult(
                    _ToPublishedResult(_ReplayOrConflictRowOperation(prior, attempt), request, isReplay: true)
                );
            }

            var now = timeProvider.GetUtcNow();
            MemoryMessage? row = null;
            var outcome = InboxOperationOutcome.NotFound;

            if (PublishedMessages.TryGetValue(request.StorageId, out var candidate))
            {
                lock (candidate)
                {
                    if (
                        PublishedMessages.TryGetValue(request.StorageId, out var current)
                        && ReferenceEquals(current, candidate)
                    )
                    {
                        row = current;
                        outcome = MessagingOperationEvaluator.EvaluatePublished(
                            operationType,
                            request.ExpectedChangedAt,
                            new PublishedOperationState(
                                current.StatusName,
                                current.NextRetryAt is not null,
                                current.LockedUntil is not null && current.LockedUntil > now,
                                current.ChangedAt
                            )
                        );

                        if (outcome == InboxOperationOutcome.Applied)
                        {
                            if (operationType == MessagingOperationType.Resend)
                            {
                                current.NextRetryAt = now;
                                current.Retries = 0;
                                current.InlineAttempts = 0;
                                current.ExpiresAt = null;
                                current.LockedUntil = null;
                                current.Owner = null;
                                _StampChange(current);
                            }
                            else if (
                                !PublishedMessages.TryRemove(
                                    new KeyValuePair<Guid, MemoryMessage>(request.StorageId, current)
                                )
                            )
                            {
                                outcome = InboxOperationOutcome.StateConflict;
                            }
                        }
                    }
                }
            }

            var receipt = attempt with
            {
                Outcome = outcome,
                CreatedAt = now,
                MessageName = row?.Name,
                MessageId = row is null ? null : _ReadMessageId(row) ?? string.Empty,
                Lane = row?.Lane.ToString(),
            };
            _RecordRowOperation(receipt);

            return ValueTask.FromResult(_ToPublishedResult(receipt, request, isReplay: false));
        }
    }

    private static PublishedOperationResult _ToPublishedResult(
        InMemoryOperationReceipt receipt,
        PublishedOperationRequest request,
        bool isReplay
    ) => _ToPublishedResult(receipt, request.StorageId, isReplay);

    private static PublishedOperationResult _ToPublishedResult(
        InMemoryOperationReceipt receipt,
        Guid storageId,
        bool isReplay
    ) =>
        new(
            receipt.OperationId,
            receipt.OperationType,
            receipt.Outcome,
            storageId,
            receipt.MessageName,
            receipt.MessageId,
            receipt.Lane is { } lane ? Enum.Parse<MessageLane>(lane) : null,
            receipt.Actor,
            receipt.Reason,
            receipt.CreatedAt,
            isReplay
        );

    // The in-memory storage is always the primary: it holds the ledger and is never an additional outbox.
    ValueTask<PublishedOperationResult> IAdditionalOutboxOperations.ApplyPublishedWithoutReceiptAsync(
        MessagingOperationType operationType,
        PublishedOperationRequest request,
        CancellationToken cancellationToken
    ) =>
        throw new InvalidOperationException(
            "This storage holds the operator ledger: re-send or delete through its published operations API."
        );

    ValueTask<PublishedOperationResult> IAdditionalOutboxOperations.RecordOutboxReceiptAsync(
        string outbox,
        PublishedOperationResult applied,
        CancellationToken cancellationToken
    )
    {
        cancellationToken.ThrowIfCancellationRequested();
        Argument.IsNotNullOrWhiteSpace(outbox);
        Argument.IsNotNull(applied);

        if (applied.OperationType is not (MessagingOperationType.Resend or MessagingOperationType.Purge))
        {
            throw new ArgumentOutOfRangeException(
                nameof(applied),
                applied.OperationType,
                "A published-row operation re-sends or deletes."
            );
        }

        var receipt = new InMemoryOperationReceipt(
            applied.OperationId,
            MessagingOperationTargetKind.Published,
            applied.OperationType,
            applied.Outcome,
            applied.Actor,
            applied.Reason,
            applied.CreatedAt,
            StorageId: applied.StorageId,
            MessageName: applied.MessageName,
            MessageId: applied.MessageId,
            Lane: applied.Lane?.ToString(),
            OutboxName: outbox
        );

        lock (_receivedUpsertLock)
        {
            if (_inboxOperationReceipts.TryGetValue(applied.OperationId, out var prior))
            {
                return ValueTask.FromResult(
                    _ToPublishedResult(_ReplayOrConflictRowOperation(prior, receipt), applied.StorageId, isReplay: true)
                );
            }

            _RecordRowOperation(receipt);

            return ValueTask.FromResult(_ToPublishedResult(receipt, applied.StorageId, isReplay: false));
        }
    }

    /// <summary>
    /// Returns what a request under an already used operation id gets: the stored receipt when the request repeats it,
    /// otherwise a conflict, audited, that changes nothing. A receipt of another target kind never replays, even for an
    /// operation type both kinds share.
    /// </summary>
    private InMemoryOperationReceipt _ReplayOrConflictRowOperation(
        InMemoryOperationReceipt prior,
        InMemoryOperationReceipt attempt
    )
    {
        var matches =
            prior.TargetKind == attempt.TargetKind
            && prior.OperationType == attempt.OperationType
            && prior.StorageId == attempt.StorageId
            && prior.ExpectedDueAt == attempt.ExpectedDueAt
            && string.Equals(prior.OutboxName, attempt.OutboxName, StringComparison.Ordinal)
            && string.Equals(prior.Actor, attempt.Actor, StringComparison.Ordinal)
            && string.Equals(prior.Reason, attempt.Reason, StringComparison.Ordinal);
        if (matches)
        {
            return prior;
        }

        var conflictNow = timeProvider.GetUtcNow();
        _inboxAudit.Add(
            new InMemoryInboxAudit(
                guidGenerator.Create(),
                attempt.OperationId,
                attempt.TargetKind,
                IncarnationId: null,
                attempt.OperationType,
                attempt.Actor,
                attempt.Reason,
                InboxOperationOutcome.OperationConflict,
                conflictNow
            )
        );

        return attempt with
        {
            Outcome = InboxOperationOutcome.OperationConflict,
            CreatedAt = conflictNow,
            MessageName = prior.MessageName,
            MessageId = prior.MessageId,
            Lane = prior.Lane,
        };
    }

    /// <summary>Stores a row operation's receipt and its audit entry; the caller holds the store lock.</summary>
    private void _RecordRowOperation(InMemoryOperationReceipt receipt)
    {
        _inboxOperationReceipts.Add(receipt.OperationId, receipt);
        _inboxAudit.Add(
            new InMemoryInboxAudit(
                guidGenerator.Create(),
                receipt.OperationId,
                receipt.TargetKind,
                IncarnationId: null,
                receipt.OperationType,
                receipt.Actor,
                receipt.Reason,
                receipt.Outcome,
                receipt.CreatedAt
            )
        );
    }
}
