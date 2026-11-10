// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Security.Claims;

namespace Headless.Messaging;

#pragma warning disable MA0048 // A topic file: its types are peers with no main type, so the file is named for the topic.
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

/// <summary>Target kind for generalized messaging operations.</summary>
[PublicAPI]
public enum MessagingOperationTargetKind
{
    Inbox = 0,
    ScheduledDelivery = 1,

    /// <summary>A published row, re-sent or deleted through <see cref="IPublishedOperationsApi"/>.</summary>
    Published = 2,
}

/// <summary>Authenticated authority shared by safe queries and audited mutations.</summary>
[PublicAPI]
public sealed record OperatorAuthorizationContext(ClaimsPrincipal Principal)
{
    internal const int ActorMaxLength = 200;

    public string Actor => Principal.Identity?.Name ?? string.Empty;

    /// <summary>
    /// Checks the authority a read needs: an authenticated principal. A viewer without a named operator can read
    /// counts and lists; only a write needs the actor <see cref="Validate"/> checks.
    /// </summary>
    /// <exception cref="UnauthorizedAccessException">The principal is not authenticated.</exception>
    public void ValidateRead()
    {
        if (Principal?.Identity?.IsAuthenticated is not true)
        {
            throw new UnauthorizedAccessException("Messaging operation reads require an authenticated principal.");
        }
    }

    /// <summary>Checks the authority a write needs: an authenticated principal with a usable actor name.</summary>
    /// <exception cref="UnauthorizedAccessException">The principal is not authenticated or has no usable actor name.</exception>
    public void Validate()
    {
        if (Principal?.Identity?.IsAuthenticated is not true)
        {
            throw new UnauthorizedAccessException("Messaging operations require an authenticated principal.");
        }

        if (string.IsNullOrWhiteSpace(Actor) || Actor.Length > ActorMaxLength)
        {
            throw new UnauthorizedAccessException(
                $"The authenticated operator actor must have a name between 1 and {ActorMaxLength} characters."
            );
        }
    }
}

/// <summary>Audited mutation request fenced to one immutable generation incarnation and state.</summary>
/// <remarks>
/// <see cref="Reason"/> is optional for a single-row request: an empty or whitespace reason is stored as
/// <see langword="null"/>. A caller that runs an Operation over a filter is expected to require one.
/// </remarks>
[PublicAPI]
public sealed record InboxOperationRequest(
    Guid OperationId,
    Guid ExpectedIncarnationId,
    StatusName ExpectedStatus,
    string? Reason,
    OperatorAuthorizationContext Authorization
)
{
    internal const int ReasonMaxLength = 1000;

    public string Actor => Authorization?.Actor ?? string.Empty;

    /// <summary>Gets the reason as it is stored: <see langword="null"/> when empty or whitespace.</summary>
    public string? StoredReason => OperatorReason.Normalize(Reason);

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

        if (Reason?.Length > ReasonMaxLength)
        {
            throw new InvalidOperationException($"Inbox operation reason cannot exceed {ReasonMaxLength} characters.");
        }

        if (Authorization is null)
        {
            throw new UnauthorizedAccessException("Inbox operations require an authorization context.");
        }

        Authorization.Validate();
    }
}

[PublicAPI]
public enum MessagingOperationType
{
    Hold = 0,
    ReleaseHold = 1,
    ForceReprocess = 2,
    Purge = 3,
    Cleanup = 4,
    Revoke = 5,
    DispatchNow = 6,

    /// <summary>A published row that failed for good was sent again with a fresh retry budget.</summary>
    Resend = 7,
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
    MessagingOperationType OperationType,
    InboxOperationOutcome Outcome,
    Guid ExpectedIncarnationId,
    StatusName ExpectedStatus,
    Guid? StorageId,
    Guid? ChildStorageId,
    long? ChildGeneration,
    Guid? ChildIncarnationId,
    string Actor,
    string? Reason,
    DateTimeOffset CreatedAt,
    bool IsReplay = false
);

/// <summary>The reason an operator gave, as storage keeps it.</summary>
internal static class OperatorReason
{
    /// <summary>Returns <see langword="null"/> for an empty or whitespace reason, else the reason unchanged.</summary>
    public static string? Normalize(string? reason) => string.IsNullOrWhiteSpace(reason) ? null : reason;
}

/// <summary>
/// Filters for the operator ledger: the receipts every audited intervention writes, refused ones included. A filter
/// left <see langword="null"/> matches every receipt; text filters compare ordinally.
/// </summary>
/// <remarks>This record grows by <see langword="init"/> members.</remarks>
[PublicAPI]
public sealed record MessagingOperationReceiptQuery
{
    /// <summary>The most receipts one read returns.</summary>
    public const int MaxLimit = 500;

    /// <summary>Gets the operator every receipt read must name.</summary>
    public string? Actor { get; init; }

    /// <summary>Gets the intervention every receipt read must record.</summary>
    public MessagingOperationType? OperationType { get; init; }

    /// <summary>Gets the kind of target every receipt read must record.</summary>
    public MessagingOperationTargetKind? TargetKind { get; init; }

    /// <summary>Gets the inclusive lower bound of the receipt time.</summary>
    public DateTimeOffset? From { get; init; }

    /// <summary>Gets the exclusive upper bound of the receipt time.</summary>
    public DateTimeOffset? Before { get; init; }

    /// <summary>Gets the position of the last receipt an earlier read returned, exclusive, or <see langword="null"/>.</summary>
    public MessagingOperationReceiptPosition? After { get; init; }

    /// <summary>Gets the most receipts to return, clamped to 1 through <see cref="MaxLimit"/>.</summary>
    public int Limit { get; init; } = 50;
}

/// <summary>The keyset position of one receipt in the ledger: its time, then its operation identifier.</summary>
/// <param name="CreatedAt">The receipt's time.</param>
/// <param name="OperationId">The receipt's operation identifier.</param>
[PublicAPI]
[System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Auto)]
public readonly record struct MessagingOperationReceiptPosition(DateTimeOffset CreatedAt, Guid OperationId);

/// <summary>One receipt in the operator ledger.</summary>
/// <param name="OperationId">The operation identifier the receipt answers.</param>
/// <param name="TargetKind">The kind of row the operation targeted.</param>
/// <param name="OperationType">What the operation did.</param>
/// <param name="Outcome">The operation's outcome.</param>
/// <param name="Actor">The operator who ran the operation.</param>
/// <param name="Reason">The reason the operator gave, or <see langword="null"/>.</param>
/// <param name="CreatedAt">The receipt's time.</param>
/// <param name="StorageId">The storage identifier of the targeted row, or <see langword="null"/>.</param>
/// <param name="IncarnationId">The targeted delivery incarnation, or <see langword="null"/>.</param>
/// <param name="ChildStorageId">The storage identifier of a row the operation created, or <see langword="null"/>.</param>
/// <param name="MessageName">The targeted message's name, or <see langword="null"/>.</param>
/// <param name="MessageId">The targeted message's identifier, or <see langword="null"/>.</param>
/// <param name="OutboxName">
/// The additional outbox whose row the operation changed, or <see langword="null"/> when the row is in the main store
/// that holds this ledger.
/// </param>
[PublicAPI]
public sealed record MessagingOperationReceiptView(
    Guid OperationId,
    MessagingOperationTargetKind TargetKind,
    MessagingOperationType OperationType,
    InboxOperationOutcome Outcome,
    string Actor,
    string? Reason,
    DateTimeOffset CreatedAt,
    Guid? StorageId,
    Guid? IncarnationId,
    Guid? ChildStorageId,
    string? MessageName,
    string? MessageId,
    string? OutboxName
);

/// <summary>One page of the operator ledger, newest first, and the position of the next page.</summary>
/// <remarks>This record grows by <see langword="init"/> members.</remarks>
[PublicAPI]
public sealed record MessagingOperationReceiptPage
{
    /// <summary>Gets the receipts, newest first.</summary>
    public required IReadOnlyList<MessagingOperationReceiptView> Items { get; init; }

    /// <summary>Gets the position to read the next page after, or <see langword="null"/> when this page is the last.</summary>
    public MessagingOperationReceiptPosition? Next { get; init; }
}
