// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Diagnostics.CodeAnalysis;
using System.Security.Claims;
using Headless.Dashboard;

namespace Headless.Messaging.Dashboard;

internal static class DashboardOperatorAuthority
{
    /// <summary>
    /// Resolves the operator <paramref name="user"/> is recorded as: the name, else the name-identifier claim, else
    /// <c>sub</c>. A user not signed in, or signed in under a placeholder name such as <c>anonymous</c>, names no
    /// operator, so a write that must be audited is refused.
    /// </summary>
    public static OperatorAuthorityResult Resolve(ClaimsPrincipal user)
    {
        if (user.Identity is not { IsAuthenticated: true } identity)
        {
            return OperatorAuthorityResult.Unauthenticated();
        }

        string? candidateActor = null;

        if (!string.IsNullOrWhiteSpace(identity.Name))
        {
            candidateActor = identity.Name;
        }
        else if (identity is ClaimsIdentity claimsIdentity)
        {
            candidateActor =
                claimsIdentity
                    .FindFirst(claim =>
                        string.Equals(claim.Type, ClaimTypes.NameIdentifier, StringComparison.Ordinal)
                        && !string.IsNullOrWhiteSpace(claim.Value)
                    )
                    ?.Value
                ?? claimsIdentity
                    .FindFirst(claim =>
                        string.Equals(claim.Type, "sub", StringComparison.Ordinal)
                        && !string.IsNullOrWhiteSpace(claim.Value)
                    )
                    ?.Value;
        }

        if (
            string.IsNullOrWhiteSpace(candidateActor)
            || string.Equals(candidateActor, "anonymous", StringComparison.OrdinalIgnoreCase)
            || string.Equals(candidateActor, "host-user", StringComparison.OrdinalIgnoreCase)
        )
        {
            return OperatorAuthorityResult.PlaceholderActor();
        }

        var principal = new ClaimsPrincipal(user.Identities.Select(static id => id.Clone()));
        var auditIdentity = (ClaimsIdentity)principal.Identity!;
        foreach (var claim in auditIdentity.FindAll(auditIdentity.NameClaimType).ToArray())
        {
            auditIdentity.RemoveClaim(claim);
        }

        auditIdentity.AddClaim(new Claim(auditIdentity.NameClaimType, candidateActor));

        return OperatorAuthorityResult.Success(new OperatorAuthorizationContext(principal));
    }
}

internal enum OperatorAuthorityStatus
{
    Success,
    Unauthenticated,
    PlaceholderActor,
}

internal sealed record OperatorAuthorityResult(
    OperatorAuthorityStatus Status,
    OperatorAuthorizationContext? Authorization
)
{
    [MemberNotNullWhen(true, nameof(Authorization))]
    public bool IsSuccess => Status == OperatorAuthorityStatus.Success && Authorization is not null;

    public static OperatorAuthorityResult Success(OperatorAuthorizationContext authorization) =>
        new(OperatorAuthorityStatus.Success, authorization);

    public static OperatorAuthorityResult Unauthenticated() =>
        new(OperatorAuthorityStatus.Unauthenticated, Authorization: null);

    public static OperatorAuthorityResult PlaceholderActor() =>
        new(OperatorAuthorityStatus.PlaceholderActor, Authorization: null);
}
