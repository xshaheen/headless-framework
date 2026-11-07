// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;

namespace Headless.Messaging.Storage.InMemory;

// The inbox reads a dashboard needs beyond the single-row operations: capped counts, an Operation's frozen scope, and
// the operator ledger. Each reads under the store lock the single-row operations write under.
internal sealed partial class InMemoryDataStorage
{
    private const int _MaxOperationChunkSize = 500;

    private ValueTask<MessageCount> _CountInboxAsync(
        InboxGenerationQuery query,
        OperatorAuthorizationContext authorization,
        int cap,
        CancellationToken cancellationToken
    )
    {
        cancellationToken.ThrowIfCancellationRequested();
        authorization.ValidateRead();
        Argument.IsPositive(cap);

        lock (_receivedUpsertLock)
        {
            return ValueTask.FromResult(MessageCount.Of(_InboxRows(query).Take(cap + 1).Count(), cap));
        }
    }

    private ValueTask<MessageCount> _CountOperationScopeAsync(
        MessageOperationScope scope,
        int cap,
        CancellationToken cancellationToken
    )
    {
        cancellationToken.ThrowIfCancellationRequested();
        Argument.IsPositive(cap);
        var inScope = _OperationScopePredicate(scope);

        lock (_receivedUpsertLock)
        {
            return ValueTask.FromResult(
                MessageCount.Of(ReceivedMessages.Values.Where(inScope).Take(cap + 1).Count(), cap)
            );
        }
    }

    private ValueTask<MessageOperationChunk> _ReadOperationScopeAsync(
        MessageOperationScope scope,
        Guid? afterId,
        int chunkSize,
        CancellationToken cancellationToken
    )
    {
        cancellationToken.ThrowIfCancellationRequested();
        Argument.IsInclusiveBetween(chunkSize, 1, _MaxOperationChunkSize);
        var inScope = _OperationScopePredicate(scope);

        lock (_receivedUpsertLock)
        {
            var targets = ReceivedMessages
                .Values.Where(message =>
                    inScope(message) && (afterId is not { } after || message.StorageId.CompareTo(after) > 0)
                )
                .OrderBy(static message => message.StorageId)
                .Take(chunkSize)
                .Select(static message => new MessageOperationTarget(
                    message.StorageId,
                    message.InboxGeneration!.IncarnationId,
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

    private static Func<MemoryMessage, bool> _OperationScopePredicate(MessageOperationScope scope)
    {
        Argument.IsNotNull(scope);
        Argument.IsNotNull(scope.Query);

        if (scope.Query.MessageType != MessageType.Subscribe)
        {
            throw new ArgumentException("An Operation scope reads received inbox generations only.", nameof(scope));
        }

        Func<MemoryMessage, bool> eligible = scope.Kind switch
        {
            MessageOperationKind.ReExecute => static message => message.IsCurrentGeneration,
            MessageOperationKind.Delete => static message => !message.IsHeld,
            _ => throw new ArgumentOutOfRangeException(nameof(scope), scope.Kind, "Unknown Operation kind."),
        };

        return message =>
            message.InboxGeneration is not null
            && InMemoryMessageFilter.Matches(message, scope.Query)
            && InMemoryMessageFilter.IsTerminal(message)
            && message.ChangedAt <= scope.HighWaterMark
            && eligible(message);
    }

    private ValueTask<MessagingOperationReceiptPage> _QueryReceiptsAsync(
        MessagingOperationReceiptQuery query,
        OperatorAuthorizationContext authorization,
        CancellationToken cancellationToken
    )
    {
        cancellationToken.ThrowIfCancellationRequested();
        Argument.IsNotNull(query);
        authorization.ValidateRead();
        var limit = Math.Clamp(query.Limit, 1, MessagingOperationReceiptQuery.MaxLimit);

        lock (_receivedUpsertLock)
        {
            var rows = _inboxOperationReceipts
                .Values.Where(receipt =>
                    (query.Actor is null || string.Equals(receipt.Actor, query.Actor, StringComparison.Ordinal))
                    && (query.OperationType is not { } type || receipt.OperationType == type)
                    && (query.TargetKind is not { } kind || receipt.TargetKind == kind)
                    && (query.From is not { } from || receipt.CreatedAt >= from)
                    && (query.Before is not { } before || receipt.CreatedAt < before)
                    && (
                        query.After is not { } after
                        || receipt.CreatedAt < after.CreatedAt
                        || (
                            receipt.CreatedAt == after.CreatedAt && receipt.OperationId.CompareTo(after.OperationId) < 0
                        )
                    )
                )
                .OrderByDescending(static receipt => receipt.CreatedAt)
                .ThenByDescending(static receipt => receipt.OperationId)
                .Take(limit + 1)
                .Select(static receipt => new MessagingOperationReceiptView(
                    receipt.OperationId,
                    receipt.TargetKind,
                    receipt.OperationType,
                    receipt.Outcome,
                    receipt.Actor,
                    receipt.Reason,
                    receipt.CreatedAt,
                    receipt.StorageId,
                    receipt.GenerationIncarnationId,
                    receipt.ChildStorageId,
                    receipt.MessageName,
                    receipt.MessageId
                ))
                .ToList();

            var page = rows.Count > limit ? rows.GetRange(0, limit) : rows;
            return ValueTask.FromResult(
                new MessagingOperationReceiptPage
                {
                    Items = page,
                    Next =
                        rows.Count > limit
                            ? new MessagingOperationReceiptPosition(page[^1].CreatedAt, page[^1].OperationId)
                            : null,
                }
            );
        }
    }
}
