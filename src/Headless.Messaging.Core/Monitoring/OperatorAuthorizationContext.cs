// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Security.Claims;
using Headless.Messaging.Messages;

namespace Headless.Messaging.Monitoring;

/// <summary>Authenticated authority shared by safe queries and audited mutations.</summary>
[PublicAPI]
public sealed record OperatorAuthorizationContext(ClaimsPrincipal Principal)
{
    internal const int ActorMaxLength = 200;

    public string Actor => Principal.Identity?.Name ?? string.Empty;

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
