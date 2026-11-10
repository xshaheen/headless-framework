// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;
using Headless.Fencing;
using Headless.Messaging.Internal;
using Headless.Messaging.Persistence;
using Headless.Primitives;

namespace Headless.Messaging.Storage.InMemory;

internal sealed partial class InMemoryDataStorage
{
    private sealed class InMemoryScheduledDeliveryOperationsApi(InMemoryDataStorage storage)
        : IScheduledDeliveryOperationsApi
    {
        public ValueTask<IndexPage<ScheduledDeliveryView>> QueryAsync(
            ScheduledDeliveryQuery query,
            OperatorAuthorizationContext authorization,
            CancellationToken cancellationToken = default
        ) => storage._QueryScheduledAsync(query, authorization, cancellationToken);

        public ValueTask<MessageCount> CountAsync(
            ScheduledDeliveryQuery query,
            OperatorAuthorizationContext authorization,
            int cap = MessageCount.DefaultCap,
            CancellationToken cancellationToken = default
        ) => storage._CountScheduledAsync(query, authorization, cap, cancellationToken);

        public ValueTask<ScheduledDeliveryOperationResult> RevokeAsync(
            ScheduledDeliveryOperationRequest request,
            CancellationToken cancellationToken = default
        ) => storage._MutateScheduledAsync(MessagingOperationType.Revoke, request, cancellationToken);

        public ValueTask<ScheduledDeliveryOperationResult> DispatchNowAsync(
            ScheduledDeliveryOperationRequest request,
            CancellationToken cancellationToken = default
        ) => storage._MutateScheduledAsync(MessagingOperationType.DispatchNow, request, cancellationToken);
    }

    public IScheduledDeliveryOperationsApi GetScheduledDeliveryOperationsApi() =>
        new InMemoryScheduledDeliveryOperationsApi(this);

    private ValueTask<IndexPage<ScheduledDeliveryView>> _QueryScheduledAsync(
        ScheduledDeliveryQuery query,
        OperatorAuthorizationContext authorization,
        CancellationToken cancellationToken
    )
    {
        cancellationToken.ThrowIfCancellationRequested();
        authorization.ValidateRead();
        query.Validate();

        var page = Math.Max(query.CurrentPage, 0);
        var pageSize = Math.Clamp(query.PageSize, 1, 200);

        lock (_receivedUpsertLock)
        {
            var now = timeProvider.GetUtcNow();
            var rows = _ScheduledRows(query);

            var materialized = rows.OrderBy(message => message.ExpiresAt).ThenBy(message => message.StorageId).ToList();

            var items = materialized
                .Skip(page * pageSize)
                .Take(pageSize)
                .Select(message => new ScheduledDeliveryView(
                    message.StorageId,
                    _ReadMessageId(message) ?? string.Empty,
                    message.Name,
                    message.Lane,
                    message.ExpiresAt ?? DateTimeOffset.MinValue,
                    "Pending",
                    _HasLiveLease(message, now),
                    message.Lease.State == RowLeaseState.Active ? message.Lease.Holder : null,
                    message.Lease.State == RowLeaseState.Active ? message.Lease.ExpiresAt : null,
                    message.InlineAttempts
                ))
                .ToList();

            return ValueTask.FromResult(
                new IndexPage<ScheduledDeliveryView>(items, page, pageSize, materialized.Count)
            );
        }
    }

    /// <summary>The pending scheduled deliveries <paramref name="query"/> matches; the caller holds the store lock.</summary>
    private IEnumerable<MemoryMessage> _ScheduledRows(ScheduledDeliveryQuery query)
    {
        var rows = PublishedMessages.Values.Where(message =>
            string.Equals(message.Version, messagingOptions.Value.Version, StringComparison.Ordinal)
            && message.StatusName is StatusName.Delayed or StatusName.Queued
            && message.InlineAttempts == 0
            && message.Retries == 0
            && message.NextRetryAt is null
            && message.ExpiresAt is not null
            && message.Lane is MessageLane.Bus or MessageLane.Queue
        );

        if (!string.IsNullOrWhiteSpace(query.MessageName))
        {
            rows = rows.Where(message => string.Equals(message.Name, query.MessageName, StringComparison.Ordinal));
        }

        if (query.Lane is { } lane)
        {
            rows = rows.Where(message => message.Lane == lane);
        }

        if (query.DueFrom is { } dueFrom)
        {
            rows = rows.Where(message => message.ExpiresAt >= dueFrom);
        }

        if (query.DueTo is { } dueTo)
        {
            rows = rows.Where(message => message.ExpiresAt <= dueTo);
        }

        if (query.StorageIds is { Count: > 0 } ids)
        {
            var idSet = ids as HashSet<Guid> ?? [.. ids];
            rows = rows.Where(message => idSet.Contains(message.StorageId));
        }

        return rows;
    }

    private ValueTask<MessageCount> _CountScheduledAsync(
        ScheduledDeliveryQuery query,
        OperatorAuthorizationContext authorization,
        int cap,
        CancellationToken cancellationToken
    )
    {
        cancellationToken.ThrowIfCancellationRequested();
        authorization.ValidateRead();
        query.Validate();
        Argument.IsPositive(cap);

        lock (_receivedUpsertLock)
        {
            return ValueTask.FromResult(MessageCount.Of(_ScheduledRows(query).Take(cap + 1).Count(), cap));
        }
    }

    private ValueTask<ScheduledDeliveryOperationResult> _MutateScheduledAsync(
        MessagingOperationType operationType,
        ScheduledDeliveryOperationRequest request,
        CancellationToken cancellationToken
    )
    {
        cancellationToken.ThrowIfCancellationRequested();
        request.Validate();

        lock (_receivedUpsertLock)
        {
            var attempt = new InMemoryOperationReceipt(
                request.OperationId,
                MessagingOperationTargetKind.ScheduledDelivery,
                operationType,
                default,
                request.Actor,
                request.StoredReason,
                default,
                ExpectedDueAt: request.ExpectedDueAt,
                StorageId: request.StorageId
            );
            if (_inboxOperationReceipts.TryGetValue(request.OperationId, out var prior))
            {
                return ValueTask.FromResult(
                    _ToScheduledResult(_ReplayOrConflictRowOperation(prior, attempt), request, isReplay: true)
                );
            }

            var now = timeProvider.GetUtcNow();
            ScheduledDeliveryOperationState? state = null;
            MemoryMessage? row = null;
            InboxOperationOutcome? outcome = null;

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
                        var hasLiveLease = _HasLiveLease(current, now);
                        state = new ScheduledDeliveryOperationState(
                            current.StatusName,
                            current.InlineAttempts,
                            current.Retries,
                            current.NextRetryAt,
                            hasLiveLease,
                            messagingOptions.Value.Version,
                            current.Version,
                            current.ExpiresAt
                        );
                        outcome = MessagingOperationEvaluator.Evaluate(operationType, request.ExpectedDueAt, state);
                        if (outcome == InboxOperationOutcome.Applied)
                        {
                            if (operationType == MessagingOperationType.Revoke)
                            {
                                PublishedMessages.TryRemove(
                                    new KeyValuePair<Guid, MemoryMessage>(request.StorageId, row)
                                );
                            }
                            else if (operationType == MessagingOperationType.DispatchNow)
                            {
                                row.StatusName = StatusName.Delayed;
                                row.ExpiresAt = now;
                                row.Lease = RowLeaseRules.Revoke(row.Lease).Next;
                                _StampChange(row);
                            }
                        }
                    }
                }
            }

            var finalOutcome =
                outcome ?? MessagingOperationEvaluator.Evaluate(operationType, request.ExpectedDueAt, state);

            var messageName = row?.Name;
            var messageId = row is null ? null : _ReadMessageId(row);
            var messageLane = row?.Lane;

            var receipt = attempt with
            {
                Outcome = finalOutcome,
                CreatedAt = now,
                MessageName = messageName,
                MessageId = messageId,
                Lane = messageLane?.ToString(),
            };
            _RecordRowOperation(receipt);

            if (finalOutcome == InboxOperationOutcome.Applied && messageLane is { } appliedLane)
            {
                MessagingMetrics.RecordScheduledOperation(operationType, appliedLane, finalOutcome, "InMemory");
            }

            return ValueTask.FromResult(_ToScheduledResult(receipt, request, isReplay: false));
        }
    }

    private static ScheduledDeliveryOperationResult _ToScheduledResult(
        InMemoryOperationReceipt receipt,
        ScheduledDeliveryOperationRequest request,
        bool isReplay
    ) =>
        new(
            receipt.OperationId,
            receipt.OperationType,
            receipt.Outcome,
            request.StorageId,
            request.ExpectedDueAt,
            receipt.MessageName,
            receipt.MessageId,
            receipt.Lane is { } lane ? Enum.Parse<MessageLane>(lane) : null,
            receipt.Actor,
            receipt.Reason,
            receipt.CreatedAt,
            isReplay
        );

    /// <summary>
    /// The row's message id header: <see langword="null"/> when the row carries none, and <see cref="string.Empty"/>
    /// when the header holds no value.
    /// </summary>
    private static string? _ReadMessageId(MemoryMessage message) =>
        message.Origin?.Headers.TryGetValue(Headers.MessageId, out var messageId) == true
            ? messageId ?? string.Empty
            : null;
}
