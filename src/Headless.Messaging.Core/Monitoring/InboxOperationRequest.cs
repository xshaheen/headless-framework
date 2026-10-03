// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Security.Claims;
using Headless.Messaging.Messages;

namespace Headless.Messaging.Monitoring;

/// <summary>Audited mutation request fenced to one immutable generation incarnation and state.</summary>
[PublicAPI]
public sealed record InboxOperationRequest(
    Guid OperationId,
    Guid ExpectedIncarnationId,
    StatusName ExpectedStatus,
    string Reason,
    OperatorAuthorizationContext Authorization
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
