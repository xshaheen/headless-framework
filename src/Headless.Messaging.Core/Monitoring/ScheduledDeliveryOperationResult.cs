// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Messaging.Messages;

namespace Headless.Messaging.Monitoring;

/// <summary>Durable result of an audited scheduled delivery mutation.</summary>
[PublicAPI]
public sealed record ScheduledDeliveryOperationResult(
    Guid OperationId,
    MessagingOperationType OperationType,
    InboxOperationOutcome Outcome,
    Guid StorageId,
    DateTimeOffset ExpectedDueAt,
    string? MessageName,
    string? MessageId,
    MessageLane? Lane,
    string Actor,
    string Reason,
    DateTimeOffset CreatedAt,
    bool IsReplay = false
);
