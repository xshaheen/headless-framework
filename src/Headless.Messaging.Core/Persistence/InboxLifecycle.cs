// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Messaging.Messages;

namespace Headless.Messaging.Persistence;

/// <summary>The immutable logical identity of one retained inbox generation.</summary>
[PublicAPI]
public sealed record InboxKey(
    string? TenantId,
    string MessageId,
    MessageLane Lane,
    string ContractIdentity,
    string ContractVersion,
    string ConsumerIdentity,
    long Generation
);

/// <summary>The immutable identity allocated when an inbox generation is first admitted.</summary>
[PublicAPI]
public sealed record InboxGeneration(long Number, Guid IncarnationId);

/// <summary>The complete persisted generation fence required for a lifecycle mutation.</summary>
[PublicAPI]
public sealed record InboxAttemptFence(
    Guid StorageId,
    MessageLane Lane,
    long Generation,
    Guid GenerationIncarnationId,
    Guid AttemptId,
    string? Owner,
    DateTimeOffset LockedUntil
);

/// <summary>The durable outcome of converging one transport delivery on an inbox generation.</summary>
[PublicAPI]
public enum InboxAdmissionDisposition
{
    Winner = 0,
    InFlightDuplicate = 1,
    SucceededDuplicate = 2,
    TerminalFailedDuplicate = 3,
}

/// <summary>Result of the atomic admission decision.</summary>
[PublicAPI]
public sealed record InboxAdmissionResult(InboxAdmissionDisposition Disposition, MediumMessage Message)
{
    public bool ShouldDispatch => Disposition is InboxAdmissionDisposition.Winner;
}
