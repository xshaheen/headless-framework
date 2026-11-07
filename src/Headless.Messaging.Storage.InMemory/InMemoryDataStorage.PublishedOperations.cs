// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;
using Headless.Messaging.Internal;

namespace Headless.Messaging.Storage.InMemory;

// Audited re-send and delete of published rows, single-row and as Operations over a frozen scope. Each write and its
// receipt happen under the store lock the ledger read takes.
internal sealed partial class InMemoryDataStorage
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

        lock (_receivedUpsertLock)
        {
            return ValueTask.FromResult(
                MessageCount.Of(PublishedMessages.Values.Where(inScope).Take(cap + 1).Count(), cap)
            );
        }
    }

    private ValueTask<MessageOperationChunk> _ReadPublishedScopeAsync(
        MessageOperationScope scope,
        Guid? afterId,
        int chunkSize,
        CancellationToken cancellationToken
    )
    {
        cancellationToken.ThrowIfCancellationRequested();
        Argument.IsInclusiveBetween(chunkSize, 1, _MaxOperationChunkSize);
        var inScope = _PublishedScopePredicate(scope);

        lock (_receivedUpsertLock)
        {
            var targets = PublishedMessages
                .Values.Where(message =>
                    inScope(message) && (afterId is not { } after || message.StorageId.CompareTo(after) > 0)
                )
                .OrderBy(static message => message.StorageId)
                .Take(chunkSize)
                .Select(static message => new MessageOperationTarget(
                    message.StorageId,
                    Guid.Empty,
                    message.StatusName,
                    message.ChangedAt
                ))
                .ToList();

            return ValueTask.FromResult(
                new MessageOperationChunk
                {
                    Targets = targets,
                    Next = targets.Count == chunkSize ? targets[^1].StorageId : null,
                }
            );
        }
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
            && InMemoryMessageFilter.IsTerminal(message)
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
            if (_inboxOperationReceipts.TryGetValue(request.OperationId, out var prior))
            {
                return ValueTask.FromResult(_ReplayOrConflictPublished(prior, operationType, request));
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

            var messageId =
                row is null ? null
                : row.Origin?.Headers.TryGetValue(Headers.MessageId, out var msgId) == true ? msgId ?? string.Empty
                : string.Empty;

            _inboxOperationReceipts.Add(
                request.OperationId,
                new InMemoryOperationReceipt(
                    request.OperationId,
                    MessagingOperationTargetKind.Published,
                    operationType,
                    outcome,
                    request.Actor,
                    request.StoredReason,
                    now,
                    StorageId: request.StorageId,
                    MessageName: row?.Name,
                    MessageId: messageId,
                    Lane: row?.Lane.ToString()
                )
            );
            _inboxAudit.Add(
                new InMemoryInboxAudit(
                    guidGenerator.Create(),
                    request.OperationId,
                    MessagingOperationTargetKind.Published,
                    IncarnationId: null,
                    operationType,
                    request.Actor,
                    request.StoredReason,
                    outcome,
                    now
                )
            );

            return ValueTask.FromResult(
                new PublishedOperationResult(
                    request.OperationId,
                    operationType,
                    outcome,
                    request.StorageId,
                    row?.Name,
                    messageId,
                    row?.Lane,
                    request.Actor,
                    request.StoredReason,
                    now
                )
            );
        }
    }

    /// <summary>Replays the receipt a retried request left, or records the refusal of a different request under its id.</summary>
    private PublishedOperationResult _ReplayOrConflictPublished(
        InMemoryOperationReceipt prior,
        MessagingOperationType operationType,
        PublishedOperationRequest request
    )
    {
        var lane = prior.Lane is { } persisted ? Enum.Parse<MessageLane>(persisted) : (MessageLane?)null;
        var matches =
            prior.TargetKind == MessagingOperationTargetKind.Published
            && prior.OperationType == operationType
            && prior.StorageId == request.StorageId
            && string.Equals(prior.Actor, request.Actor, StringComparison.Ordinal)
            && string.Equals(prior.Reason, request.StoredReason, StringComparison.Ordinal);

        if (matches)
        {
            return new PublishedOperationResult(
                prior.OperationId,
                prior.OperationType,
                prior.Outcome,
                request.StorageId,
                prior.MessageName,
                prior.MessageId,
                lane,
                prior.Actor,
                prior.Reason,
                prior.CreatedAt,
                IsReplay: true
            );
        }

        var conflictNow = timeProvider.GetUtcNow();
        _inboxAudit.Add(
            new InMemoryInboxAudit(
                guidGenerator.Create(),
                request.OperationId,
                MessagingOperationTargetKind.Published,
                IncarnationId: null,
                operationType,
                request.Actor,
                request.StoredReason,
                InboxOperationOutcome.OperationConflict,
                conflictNow
            )
        );

        return new PublishedOperationResult(
            request.OperationId,
            operationType,
            InboxOperationOutcome.OperationConflict,
            request.StorageId,
            prior.MessageName,
            prior.MessageId,
            lane,
            request.Actor,
            request.StoredReason,
            conflictNow,
            IsReplay: true
        );
    }
}
