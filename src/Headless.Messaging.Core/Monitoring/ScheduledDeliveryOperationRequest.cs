// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Messaging.Messages;

namespace Headless.Messaging.Monitoring;

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
