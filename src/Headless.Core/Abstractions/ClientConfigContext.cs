// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Security.Claims;
using Headless.Checks;

namespace Headless.Abstractions;

/// <summary>
/// Identifies the user and tenant a client-config section is built for. Section builders resolve every
/// value for this principal and tenant rather than the ambient ones, because applications build the
/// config inside login and token-refresh responses, where the newly issued principal is not yet ambient.
/// </summary>
/// <param name="Principal">The principal the section is built for.</param>
/// <param name="TenantId">The tenant the section is built for, or <see langword="null"/> for the host.</param>
[PublicAPI]
public sealed record ClientConfigContext(ClaimsPrincipal Principal, string? TenantId)
{
    /// <summary>The principal the section is built for.</summary>
    public ClaimsPrincipal Principal { get; } = Argument.IsNotNull(Principal);
}
