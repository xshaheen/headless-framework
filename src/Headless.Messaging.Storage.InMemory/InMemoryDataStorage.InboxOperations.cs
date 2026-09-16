// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;
using Headless.Messaging.Configuration;
using Headless.Messaging.Internal;
using Headless.Messaging.Messages;
using Headless.Messaging.Monitoring;
using Headless.Messaging.Persistence;
using Headless.Primitives;

namespace Headless.Messaging.Storage.InMemory;

internal sealed partial class InMemoryDataStorage
{
    private sealed class InMemoryInboxOperationsApi(InMemoryDataStorage storage) : IInboxOperationsApi
    {
        public ValueTask<IndexPage<InboxGenerationView>> QueryAsync(
            InboxGenerationQuery query,
            OperatorAuthorizationContext authorization,
            CancellationToken cancellationToken = default
        ) => storage._QueryInboxAsync(query, authorization, cancellationToken);

        public ValueTask<InboxOperationResult> HoldAsync(
            InboxOperationRequest request,
            CancellationToken cancellationToken = default
        ) => storage._MutateInboxAsync(MessagingOperationType.Hold, request, cancellationToken);

        public ValueTask<InboxOperationResult> ReleaseHoldAsync(
            InboxOperationRequest request,
            CancellationToken cancellationToken = default
        ) => storage._MutateInboxAsync(MessagingOperationType.ReleaseHold, request, cancellationToken);

        public ValueTask<InboxOperationResult> ForceReprocessAsync(
            InboxOperationRequest request,
            CancellationToken cancellationToken = default
        ) => storage._MutateInboxAsync(MessagingOperationType.ForceReprocess, request, cancellationToken);

        public ValueTask<InboxOperationResult> PurgeAsync(
            InboxOperationRequest request,
            CancellationToken cancellationToken = default
        ) => storage._MutateInboxAsync(MessagingOperationType.Purge, request, cancellationToken);
    }

    private sealed record InMemoryInboxAudit(
        Guid AuditId,
        Guid OperationId,
        MessagingOperationTargetKind TargetKind,
        Guid? IncarnationId,
        MessagingOperationType OperationType,
        string Actor,
        string Reason,
        InboxOperationOutcome Outcome,
        DateTimeOffset CreatedAt
    );

    private sealed record InMemoryOperationReceipt(
        Guid OperationId,
        MessagingOperationTargetKind TargetKind,
        MessagingOperationType OperationType,
        InboxOperationOutcome Outcome,
        string Actor,
        string Reason,
        DateTimeOffset CreatedAt,
        Guid? GenerationIncarnationId = null,
        StatusName? ExpectedStatus = null,
        DateTimeOffset? ExpectedDueAt = null,
        Guid? StorageId = null,
        string? MessageName = null,
        string? MessageId = null,
        string? Lane = null,
        Guid? ChildStorageId = null,
        long? ChildGeneration = null,
        Guid? ChildIncarnationId = null
    );

    public IInboxOperationsApi GetInboxOperationsApi() => new InMemoryInboxOperationsApi(this);

    public ValueTask<InboxHistoryRetentionCutoffs> GetInboxHistoryRetentionCutoffsAsync(
        CancellationToken cancellationToken = default
    )
    {
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.FromResult(
            InboxHistoryRetentionCutoffs.Create(timeProvider.GetUtcNow(), messagingOptions.Value)
        );
    }

    public ValueTask<int> DeleteExpiredInboxAuditsAsync(
        InboxHistoryRetentionCutoffs cutoffs,
        int batchSize,
        CancellationToken cancellationToken = default
    )
    {
        cancellationToken.ThrowIfCancellationRequested();
        Argument.IsPositive(batchSize);
        lock (_receivedUpsertLock)
        {
            var candidates = _inboxAudit
                .Where(x =>
                    x.CreatedAt
                    <= (
                        x.OperationType == MessagingOperationType.Cleanup ? cutoffs.CleanupAudit : cutoffs.OperatorAudit
                    )
                )
                .OrderBy(x => x.CreatedAt)
                .ThenBy(x => x.AuditId)
                .Take(batchSize)
                .ToArray();
            foreach (var candidate in candidates)
            {
                cancellationToken.ThrowIfCancellationRequested();
                _inboxAudit.Remove(candidate);
            }
            return ValueTask.FromResult(candidates.Length);
        }
    }

    public ValueTask<int> DeleteExpiredInboxReceiptsAsync(
        InboxHistoryRetentionCutoffs cutoffs,
        int batchSize,
        CancellationToken cancellationToken = default
    )
    {
        cancellationToken.ThrowIfCancellationRequested();
        Argument.IsPositive(batchSize);
        lock (_receivedUpsertLock)
        {
            var referenced = _inboxAudit.Select(x => x.OperationId).ToHashSet();
            var candidates = _inboxOperationReceipts
                .Values.Where(x =>
                    x.CreatedAt
                        <= (
                            x.OperationType == MessagingOperationType.Cleanup
                                ? cutoffs.CleanupReceipt
                                : cutoffs.OperatorReceipt
                        )
                    && !referenced.Contains(x.OperationId)
                )
                .OrderBy(x => x.CreatedAt)
                .ThenBy(x => x.OperationId)
                .Take(batchSize)
                .ToArray();
            foreach (var candidate in candidates)
            {
                cancellationToken.ThrowIfCancellationRequested();
                _inboxOperationReceipts.Remove(candidate.OperationId);
            }
            return ValueTask.FromResult(candidates.Length);
        }
    }

    private ValueTask<IndexPage<InboxGenerationView>> _QueryInboxAsync(
        InboxGenerationQuery query,
        OperatorAuthorizationContext authorization,
        CancellationToken cancellationToken
    )
    {
        cancellationToken.ThrowIfCancellationRequested();
        authorization.Validate();
        var page = Math.Max(query.CurrentPage, 0);
        var pageSize = Math.Clamp(query.PageSize, 1, 200);

        lock (_receivedUpsertLock)
        {
            IEnumerable<MemoryMessage> rows = ReceivedMessages.Values.Where(message =>
                message.InboxGeneration is not null
            );
            if (query.IncarnationId is { } incarnationId)
            {
                rows = rows.Where(message => message.InboxGeneration!.IncarnationId == incarnationId);
            }

            if (!string.IsNullOrEmpty(query.ConsumerIdentity))
            {
                rows = rows.Where(message =>
                    string.Equals(message.InboxKey!.ConsumerIdentity, query.ConsumerIdentity, StringComparison.Ordinal)
                );
            }

            if (query.Lane is { } lane)
            {
                rows = rows.Where(message => message.Lane == lane);
            }

            if (query.Status is { } status)
            {
                rows = rows.Where(message => message.StatusName == status);
            }

            if (query.IsOrphaned is { } orphaned)
            {
                rows = rows.Where(message => message.IsInboxOrphaned == orphaned);
            }

            if (query.IsHeld is { } held)
            {
                rows = rows.Where(message => message.IsHeld == held);
            }

            var materialized = rows.OrderByDescending(message => message.Added)
                .ThenBy(message => message.StorageId)
                .ToList();
            var items = materialized.Skip(page * pageSize).Take(pageSize).Select(_ToInboxGenerationView).ToList();
            return ValueTask.FromResult(new IndexPage<InboxGenerationView>(items, page, pageSize, materialized.Count));
        }
    }

    private ValueTask<InboxOperationResult> _MutateInboxAsync(
        MessagingOperationType operationType,
        InboxOperationRequest request,
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
                    prior.TargetKind == MessagingOperationTargetKind.Inbox
                    && prior.OperationType == operationType
                    && prior.GenerationIncarnationId == request.ExpectedIncarnationId
                    && prior.ExpectedStatus == request.ExpectedStatus
                    && string.Equals(prior.Actor, request.Actor, StringComparison.Ordinal)
                    && string.Equals(prior.Reason, request.Reason, StringComparison.Ordinal);
                if (matches)
                {
                    return ValueTask.FromResult(
                        new InboxOperationResult(
                            prior.OperationId,
                            prior.OperationType,
                            prior.Outcome,
                            request.ExpectedIncarnationId,
                            request.ExpectedStatus,
                            prior.StorageId,
                            prior.ChildStorageId,
                            prior.ChildGeneration,
                            prior.ChildIncarnationId,
                            prior.Actor,
                            prior.Reason,
                            prior.CreatedAt,
                            IsReplay: true
                        )
                    );
                }

                var conflictAt = timeProvider.GetUtcNow();
                var conflict = new InboxOperationResult(
                    request.OperationId,
                    operationType,
                    InboxOperationOutcome.OperationConflict,
                    request.ExpectedIncarnationId,
                    request.ExpectedStatus,
                    StorageId: null,
                    ChildStorageId: null,
                    ChildGeneration: null,
                    ChildIncarnationId: null,
                    request.Actor,
                    request.Reason,
                    conflictAt,
                    IsReplay: true
                );
                _inboxAudit.Add(
                    new InMemoryInboxAudit(
                        guidGenerator.Create(),
                        request.OperationId,
                        MessagingOperationTargetKind.Inbox,
                        request.ExpectedIncarnationId,
                        operationType,
                        request.Actor,
                        request.Reason,
                        InboxOperationOutcome.OperationConflict,
                        conflictAt
                    )
                );
                return ValueTask.FromResult(conflict);
            }

            var row = ReceivedMessages.Values.SingleOrDefault(message =>
                message.InboxGeneration?.IncarnationId == request.ExpectedIncarnationId
            );
            var now = timeProvider.GetUtcNow();
            InboxOperationState? state = row is null
                ? null
                : new InboxOperationState(
                    row.StatusName,
                    row.NextRetryAt is not null,
                    row.IsHeld,
                    row.IsCurrentGeneration,
                    row.InboxKey!.Generation,
                    row.IsInboxOrphaned,
                    row.LockedUntil > now
                );
            var outcome = MessagingOperationEvaluator.Evaluate(operationType, request.ExpectedStatus, state);
            Guid? childStorageId = null;
            long? childGeneration = null;
            Guid? childIncarnationId = null;

            if (outcome is InboxOperationOutcome.Applied && row is not null)
            {
                switch (operationType)
                {
                    case MessagingOperationType.Hold:
                        row.IsHeld = true;
                        row.HeldAt = now;
                        row.HeldBy = request.Actor;
                        row.HoldReason = request.Reason;
                        row.HoldOperationId = request.OperationId;
                        break;
                    case MessagingOperationType.ReleaseHold:
                        row.IsHeld = false;
                        row.HeldAt = null;
                        row.HeldBy = null;
                        row.HoldReason = null;
                        row.HoldOperationId = request.OperationId;
                        break;
                    case MessagingOperationType.ForceReprocess:
                        var child = _CreateForcedChild(row, request.OperationId, now);
                        row.IsCurrentGeneration = false;
                        ReceivedMessages[child.StorageId] = child;
                        childStorageId = child.StorageId;
                        childGeneration = child.InboxGeneration!.Number;
                        childIncarnationId = child.InboxGeneration.IncarnationId;
                        break;
                    case MessagingOperationType.Purge:
                        ReceivedMessages.TryRemove(row.StorageId, out _);
                        _RemoveFromIdentityIndex(row);
                        break;
                }
            }

            var receipt = new InMemoryOperationReceipt(
                request.OperationId,
                MessagingOperationTargetKind.Inbox,
                operationType,
                outcome,
                request.Actor,
                request.Reason,
                now,
                GenerationIncarnationId: request.ExpectedIncarnationId,
                ExpectedStatus: request.ExpectedStatus,
                ExpectedDueAt: null,
                StorageId: row?.StorageId,
                MessageName: null,
                MessageId: null,
                Lane: null,
                ChildStorageId: childStorageId,
                ChildGeneration: childGeneration,
                ChildIncarnationId: childIncarnationId
            );
            _inboxOperationReceipts.Add(request.OperationId, receipt);
            _inboxAudit.Add(
                new InMemoryInboxAudit(
                    guidGenerator.Create(),
                    request.OperationId,
                    MessagingOperationTargetKind.Inbox,
                    request.ExpectedIncarnationId,
                    operationType,
                    request.Actor,
                    request.Reason,
                    outcome,
                    now
                )
            );
            var result = new InboxOperationResult(
                request.OperationId,
                operationType,
                outcome,
                request.ExpectedIncarnationId,
                request.ExpectedStatus,
                row?.StorageId,
                childStorageId,
                childGeneration,
                childIncarnationId,
                request.Actor,
                request.Reason,
                now
            );
            if (row?.InboxKey is { } key && outcome is InboxOperationOutcome.Applied)
            {
                MessagingMetrics.RecordInbox(
                    operationType is MessagingOperationType.ForceReprocess
                        ? InboxMetricKind.Replay
                        : InboxMetricKind.Retention,
                    key.ConsumerIdentity,
                    key.Lane,
                    operationType switch
                    {
                        MessagingOperationType.Hold => InboxMetricOutcome.Held,
                        MessagingOperationType.ReleaseHold => InboxMetricOutcome.Released,
                        MessagingOperationType.ForceReprocess => InboxMetricOutcome.Replayed,
                        MessagingOperationType.Purge => InboxMetricOutcome.Purged,
                        _ => throw new ArgumentOutOfRangeException(nameof(operationType), operationType, message: null),
                    },
                    MessagingInboxCapabilityTier.ProcessLocal,
                    "InMemory"
                );
            }
            return ValueTask.FromResult(result);
        }
    }

    private MemoryMessage _CreateForcedChild(MemoryMessage parent, Guid operationId, DateTimeOffset now)
    {
        var parentKey = parent.InboxKey!;
        var generation = checked(parentKey.Generation + 1);
        var incarnationId = guidGenerator.Create();
        return new MemoryMessage
        {
            StorageId = guidGenerator.Create(),
            Origin = _CloneOrigin(parent.Origin),
            Content = parent.Content,
            Lane = parent.Lane,
            Name = parent.Name,
            Group = parent.Group,
            Version = parent.Version,
            Added = now,
            NextRetryAt = now.Add(messagingOptions.Value.RetryPolicy.InitialDispatchGrace),
            StatusName = StatusName.Scheduled,
            InboxKey = parentKey with { Generation = generation },
            InboxGeneration = new InboxGeneration(generation, incarnationId),
            InboxRetention = parent.InboxRetention,
            LifecycleId = parent.LifecycleId,
            ReplayParentIncarnationId = parent.InboxGeneration!.IncarnationId,
            ReplayOperationId = operationId,
        };
    }

    private static InboxGenerationView _ToInboxGenerationView(MemoryMessage message)
    {
        var key = message.InboxKey!;
        return new InboxGenerationView(
            message.StorageId,
            message.InboxGeneration!.IncarnationId,
            key.Generation,
            key.TenantId,
            key.MessageId,
            key.Lane,
            key.ContractIdentity,
            key.ContractVersion,
            key.ConsumerIdentity,
            message.StatusName,
            message.IsCurrentGeneration,
            message.IsInboxOrphaned,
            message.ReplayParentIncarnationId,
            message.ReplayOperationId,
            message.TerminalAt,
            message.EffectiveExpiresAt,
            message.IsHeld,
            message.HeldAt,
            message.HeldBy,
            message.HoldReason
        );
    }
}
