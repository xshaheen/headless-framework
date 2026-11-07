// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Messaging;

#pragma warning disable MA0048 // A topic file: its types are peers with no main type, so the file is named for the topic.

/// <summary>Audited mutation request for one published row.</summary>
/// <param name="OperationId">The request's identity; a retried request under it replays its stored receipt.</param>
/// <param name="StorageId">The published row's storage identifier.</param>
/// <param name="ExpectedChangedAt">
/// The row's change marker as the caller last read it, or <see langword="null"/> to apply to the row as it is. An
/// Operation chunk passes <see cref="MessageOperationTarget.ChangedAt"/>, so a row another write changed since the read
/// is refused instead of changed twice.
/// </param>
/// <param name="Reason">
/// The operator's reason. Optional for a single-row request: an empty or whitespace reason is stored as
/// <see langword="null"/>. A caller that runs an Operation over a filter is expected to require one.
/// </param>
/// <param name="Authorization">The operator's authority; a write needs a named operator.</param>
/// <remarks>
/// A replay matches the stored receipt on its operation type, row, actor, and reason;
/// <paramref name="ExpectedChangedAt"/> is not stored, so it takes no part.
/// </remarks>
[PublicAPI]
public sealed record PublishedOperationRequest(
    Guid OperationId,
    Guid StorageId,
    DateTimeOffset? ExpectedChangedAt,
    string? Reason,
    OperatorAuthorizationContext Authorization
)
{
    internal const int ReasonMaxLength = 1000;

    public string Actor => Authorization?.Actor ?? string.Empty;

    /// <summary>Gets the reason as it is stored: <see langword="null"/> when empty or whitespace.</summary>
    public string? StoredReason => OperatorReason.Normalize(Reason);

    /// <summary>Checks the request's authority, identities, and reason length.</summary>
    /// <exception cref="UnauthorizedAccessException">The request does not name an authenticated operator.</exception>
    /// <exception cref="InvalidOperationException">An identity is empty or the reason is too long.</exception>
    public void Validate()
    {
        if (Authorization is null)
        {
            throw new UnauthorizedAccessException("Published message operations require an authorization context.");
        }

        Authorization.Validate();

        if (OperationId == Guid.Empty)
        {
            throw new InvalidOperationException("Published message operation identity cannot be empty.");
        }

        if (StorageId == Guid.Empty)
        {
            throw new InvalidOperationException("Storage identity cannot be empty.");
        }

        if (Reason?.Length > ReasonMaxLength)
        {
            throw new InvalidOperationException(
                $"Published message operation reason cannot exceed {ReasonMaxLength} characters."
            );
        }
    }
}

/// <summary>Durable result of an audited published-row mutation.</summary>
[PublicAPI]
public sealed record PublishedOperationResult(
    Guid OperationId,
    MessagingOperationType OperationType,
    InboxOperationOutcome Outcome,
    Guid StorageId,
    string? MessageName,
    string? MessageId,
    MessageLane? Lane,
    string Actor,
    string? Reason,
    DateTimeOffset CreatedAt,
    bool IsReplay = false
);

#pragma warning restore MA0048
