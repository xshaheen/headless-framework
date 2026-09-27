// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Security.Claims;
using Headless.Checks;

namespace Headless.Abstractions;

/// <summary>
/// A principal and tenant to resolve values for, in place of the ambient ones. Readers that take it switch the
/// ambient principal and tenant to it for the duration of the read, so a caller can resolve on behalf of an
/// identity that is not ambient yet, such as the principal a login or token-refresh response is about to issue.
/// </summary>
/// <param name="Principal">The principal to resolve for.</param>
/// <param name="TenantId">The tenant to resolve for, or <see langword="null"/> for the host.</param>
[PublicAPI]
public sealed record PrincipalContext(ClaimsPrincipal Principal, string? TenantId)
{
    /// <summary>The principal to resolve for.</summary>
    public ClaimsPrincipal Principal { get; } = Argument.IsNotNull(Principal);
}
