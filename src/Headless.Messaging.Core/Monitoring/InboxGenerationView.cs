// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Security.Claims;
using Headless.Messaging.Messages;

namespace Headless.Messaging.Monitoring;

/// <summary>Safe operator projection of one retained inbox generation.</summary>
[PublicAPI]
public sealed record InboxGenerationView(
    Guid StorageId,
    Guid IncarnationId,
    long Generation,
    string? TenantId,
    string MessageId,
    MessageLane Lane,
    string ContractIdentity,
    string ContractVersion,
    string ConsumerIdentity,
    StatusName Status,
    bool IsCurrentGeneration,
    bool IsOrphaned,
    Guid? ReplayParentIncarnationId,
    Guid? ReplayOperationId,
    DateTimeOffset? TerminalAt,
    DateTimeOffset? EffectiveExpiresAt,
    bool IsHeld,
    DateTimeOffset? HeldAt,
    string? HeldBy,
    string? HoldReason
);
