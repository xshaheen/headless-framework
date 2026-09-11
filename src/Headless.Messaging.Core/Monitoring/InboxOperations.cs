// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Security.Claims;
using Headless.Messaging.Messages;

namespace Headless.Messaging.Monitoring;

/// <summary>Payload-free filters for retained inbox generations.</summary>
[PublicAPI]
public sealed class InboxGenerationQuery
{
    public Guid? IncarnationId { get; set; }

    public string? ConsumerIdentity { get; set; }

    public MessageLane? Lane { get; set; }

    public StatusName? Status { get; set; }

    public bool? IsOrphaned { get; set; }

    public bool? IsHeld { get; set; }

    public int CurrentPage { get; set; }

    public int PageSize { get; set; } = 20;
}

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

/// <summary>Authenticated authority shared by safe queries and audited mutations.</summary>
[PublicAPI]
public sealed record InboxAuthorizationContext(ClaimsPrincipal Principal)
{
    internal const int ActorMaxLength = 200;

    public string Actor => Principal.Identity?.Name ?? string.Empty;

    public void Validate()
    {
        if (Principal?.Identity?.IsAuthenticated is not true)
        {
            throw new UnauthorizedAccessException("Inbox operations require an authenticated principal.");
        }

        if (string.IsNullOrWhiteSpace(Actor) || Actor.Length > ActorMaxLength)
        {
            throw new UnauthorizedAccessException(
                $"The authenticated inbox actor must have a name between 1 and {ActorMaxLength} characters."
            );
        }
    }
}

/// <summary>Audited mutation request fenced to one immutable generation incarnation and state.</summary>
[PublicAPI]
public sealed record InboxOperationRequest(
    Guid OperationId,
    Guid ExpectedIncarnationId,
    StatusName ExpectedStatus,
    string Reason,
    InboxAuthorizationContext Authorization
)
{
    internal const int ReasonMaxLength = 1000;

    public string Actor => Authorization?.Actor ?? string.Empty;

    public void Validate()
    {
        if (OperationId == Guid.Empty)
        {
            throw new InvalidOperationException("Inbox operation identity cannot be empty.");
        }

        if (ExpectedIncarnationId == Guid.Empty)
        {
            throw new InvalidOperationException("Expected inbox incarnation cannot be empty.");
        }

        if (string.IsNullOrWhiteSpace(Reason) || Reason.Length > ReasonMaxLength)
        {
            throw new InvalidOperationException(
                $"Inbox operation reason must be between 1 and {ReasonMaxLength} characters."
            );
        }

        if (Authorization is null)
        {
            throw new UnauthorizedAccessException("Inbox operations require an authorization context.");
        }

        Authorization.Validate();
    }
}

[PublicAPI]
public enum InboxOperationType
{
    Hold = 0,
    ReleaseHold = 1,
    ForceReprocess = 2,
    Purge = 3,
    Cleanup = 4,
}

[PublicAPI]
public enum InboxOperationOutcome
{
    Applied = 0,
    NotFound = 1,
    StateConflict = 2,
    Active = 3,
    Held = 4,
    OperationConflict = 5,
}

/// <summary>Durable result of an audited inbox mutation.</summary>
[PublicAPI]
public sealed record InboxOperationResult(
    Guid OperationId,
    InboxOperationType OperationType,
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
