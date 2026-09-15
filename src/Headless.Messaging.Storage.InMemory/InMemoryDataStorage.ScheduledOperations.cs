// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Messaging.Configuration;
using Headless.Messaging.Internal;
using Headless.Messaging.Messages;
using Headless.Messaging.Monitoring;
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
        authorization.Validate();
        query.Validate();

        var page = Math.Max(query.CurrentPage, 0);
        var pageSize = Math.Clamp(query.PageSize, 1, 200);

        lock (_receivedUpsertLock)
        {
            var now = timeProvider.GetUtcNow();
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

            var materialized = rows.OrderBy(message => message.ExpiresAt).ThenBy(message => message.StorageId).ToList();

            var items = materialized
                .Skip(page * pageSize)
                .Take(pageSize)
                .Select(message => new ScheduledDeliveryView(
                    message.StorageId,
                    message.Origin?.Headers.TryGetValue(Headers.MessageId, out var msgId) == true
                        ? msgId ?? string.Empty
                        : string.Empty,
                    message.Name,
                    message.Lane,
                    message.ExpiresAt ?? DateTimeOffset.MinValue,
                    "Pending",
                    message.LockedUntil is not null && message.LockedUntil > now,
                    message.Owner,
                    message.LockedUntil,
                    message.InlineAttempts
                ))
                .ToList();

            return ValueTask.FromResult(
                new IndexPage<ScheduledDeliveryView>(items, page, pageSize, materialized.Count)
            );
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
            if (_inboxOperationReceipts.TryGetValue(request.OperationId, out var prior))
            {
                var matches =
                    prior.TargetKind == MessagingOperationTargetKind.ScheduledDelivery
                    && prior.OperationType == operationType
                    && prior.StorageId == request.StorageId
                    && prior.ExpectedDueAt == request.ExpectedDueAt
                    && string.Equals(prior.Actor, request.Actor, StringComparison.Ordinal)
                    && string.Equals(prior.Reason, request.Reason, StringComparison.Ordinal);

                if (matches)
                {
                    return ValueTask.FromResult(
                        new ScheduledDeliveryOperationResult(
                            prior.OperationId,
                            prior.OperationType,
                            prior.Outcome,
                            request.StorageId,
                            request.ExpectedDueAt,
                            prior.MessageName,
                            prior.MessageId,
                            prior.Lane is { } lane ? Enum.Parse<MessageLane>(lane) : null,
                            prior.Actor,
                            prior.Reason,
                            prior.CreatedAt,
                            IsReplay: true
                        )
                    );
                }

                var conflictNow = timeProvider.GetUtcNow();
                _inboxAudit.Add(
                    new InMemoryInboxAudit(
                        guidGenerator.Create(),
                        request.OperationId,
                        MessagingOperationTargetKind.ScheduledDelivery,
                        IncarnationId: null,
                        operationType,
                        request.Actor,
                        request.Reason,
                        InboxOperationOutcome.OperationConflict,
                        conflictNow
                    )
                );

                return ValueTask.FromResult(
                    new ScheduledDeliveryOperationResult(
                        request.OperationId,
                        operationType,
                        InboxOperationOutcome.OperationConflict,
                        request.StorageId,
                        request.ExpectedDueAt,
                        prior.MessageName,
                        prior.MessageId,
                        prior.Lane is { } conflictLane ? Enum.Parse<MessageLane>(conflictLane) : null,
                        request.Actor,
                        request.Reason,
                        conflictNow,
                        IsReplay: true
                    )
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
                        var hasLiveLease = current.LockedUntil is not null && current.LockedUntil > now;
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
                                row.LockedUntil = null;
                                row.Owner = null;
                            }
                        }
                    }
                }
            }

            var finalOutcome =
                outcome ?? MessagingOperationEvaluator.Evaluate(operationType, request.ExpectedDueAt, state);

            var messageName = row?.Name;
            var messageId =
                row?.Origin?.Headers.TryGetValue(Headers.MessageId, out var msgId) == true
                    ? msgId ?? string.Empty
                    : null;
            var messageLane = row?.Lane;

            var receipt = new InMemoryOperationReceipt(
                request.OperationId,
                MessagingOperationTargetKind.ScheduledDelivery,
                operationType,
                finalOutcome,
                request.Actor,
                request.Reason,
                now,
                ExpectedDueAt: request.ExpectedDueAt,
                StorageId: request.StorageId,
                MessageName: messageName,
                MessageId: messageId,
                Lane: messageLane?.ToString()
            );
            _inboxOperationReceipts.Add(request.OperationId, receipt);

            _inboxAudit.Add(
                new InMemoryInboxAudit(
                    guidGenerator.Create(),
                    request.OperationId,
                    MessagingOperationTargetKind.ScheduledDelivery,
                    IncarnationId: null,
                    operationType,
                    request.Actor,
                    request.Reason,
                    finalOutcome,
                    now
                )
            );

            if (finalOutcome == InboxOperationOutcome.Applied && messageLane is { } appliedLane)
            {
                MessagingMetrics.RecordScheduledOperation(operationType, appliedLane, finalOutcome, "InMemory");
            }

            return ValueTask.FromResult(
                new ScheduledDeliveryOperationResult(
                    request.OperationId,
                    operationType,
                    finalOutcome,
                    request.StorageId,
                    request.ExpectedDueAt,
                    messageName,
                    messageId,
                    messageLane,
                    request.Actor,
                    request.Reason,
                    now,
                    IsReplay: false
                )
            );
        }
    }
}
