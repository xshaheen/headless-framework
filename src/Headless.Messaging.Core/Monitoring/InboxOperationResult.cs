// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Security.Claims;
using Headless.Messaging.Messages;

namespace Headless.Messaging.Monitoring;

/// <summary>Durable result of an audited inbox mutation.</summary>
[PublicAPI]
public sealed record InboxOperationResult(
    Guid OperationId,
    MessagingOperationType OperationType,
    InboxOperationOutcome Outcome,
    Guid ExpectedIncarnationId,
    StatusName ExpectedStatus,
    Guid? StorageId,
    Guid? ChildStorageId,
    long? ChildGeneration,
    Guid? ChildIncarnationId,
    string Actor,
    string Reason,
    DateTimeOffset CreatedAt,
    bool IsReplay = false
);
