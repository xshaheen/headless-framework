// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Messaging.Messages;

namespace Headless.Messaging.Monitoring;

/// <summary>Payload-free filters for pending scheduled deliveries.</summary>
[PublicAPI]
public sealed class ScheduledDeliveryQuery
{
    public const int MaxStorageIds = 500;

    public string? MessageName { get; set; }

    public MessageLane? Lane { get; set; }

    public DateTimeOffset? DueFrom { get; set; }

    public DateTimeOffset? DueTo { get; set; }

    public IReadOnlyCollection<Guid>? StorageIds { get; set; }

    public int CurrentPage { get; set; }

    public int PageSize { get; set; } = 20;

    public void Validate()
    {
        if (StorageIds is { Count: > MaxStorageIds })
        {
            throw new InvalidOperationException($"Storage IDs filter cannot exceed {MaxStorageIds} items.");
        }
    }
}

/// <summary>Safe operator projection of one pending scheduled delivery.</summary>
[PublicAPI]
public sealed record ScheduledDeliveryView(
    Guid StorageId,
    string MessageId,
    string MessageName,
    MessageLane Lane,
    DateTimeOffset ExpectedDueAt,
    string Status,
    bool IsLeased,
    string? Owner,
    DateTimeOffset? LockedUntil,
    int InlineAttempts
);

/// <summary>Audited mutation request fenced to one immutable storage row and expected due instant.</summary>
[PublicAPI]
public sealed record ScheduledDeliveryOperationRequest(
    Guid OperationId,
    Guid StorageId,
    DateTimeOffset ExpectedDueAt,
    string Reason,
    OperatorAuthorizationContext Authorization
)
{
    internal const int ReasonMaxLength = 1000;

    public string Actor => Authorization?.Actor ?? string.Empty;

    public void Validate()
    {
        if (Authorization is null)
        {
            throw new UnauthorizedAccessException("Scheduled delivery operations require an authorization context.");
        }

        Authorization.Validate();

        if (OperationId == Guid.Empty)
        {
            throw new InvalidOperationException("Scheduled delivery operation identity cannot be empty.");
        }

        if (StorageId == Guid.Empty)
        {
            throw new InvalidOperationException("Storage identity cannot be empty.");
        }

        if (string.IsNullOrWhiteSpace(Reason) || Reason.Length > ReasonMaxLength)
        {
            throw new InvalidOperationException(
                $"Scheduled delivery operation reason must be between 1 and {ReasonMaxLength} characters."
            );
        }
    }
}

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
