// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Security.Principal;
using Framework.Checks;
using Framework.Constants;
using Framework.Primitives;

#pragma warning disable IDE0130
// ReSharper disable once CheckNamespace
namespace System.Security.Claims;

[PublicAPI]
public static class ClaimsPrincipalExtensions
{
    public static UserId? GetUserId(this ClaimsPrincipal? principal)
    {
        var id = principal?.FindFirst(FrameworkClaimTypes.UserId)?.Value;

        if (id is null)
        {
            return null;
        }

        return UserId.TryParse(id, CultureInfo.InvariantCulture, out var userId) ? userId : null;
    }

    public static UserId GetRequiredUserId(this ClaimsPrincipal? principal)
    {
        return GetUserId(principal) ?? throw new InvalidOperationException("User id is not found.");
    }

    public static UserId? GetUserId(this IIdentity identity)
    {
        var claimsIdentity = identity as ClaimsIdentity;
        var id = claimsIdentity?.FindFirst(FrameworkClaimTypes.UserId)?.Value;

        if (id is null)
        {
            return null;
        }

        return UserId.TryParse(id, CultureInfo.InvariantCulture, out var userId) ? userId : null;
    }

    public static UserId GetRequiredUserId(this IIdentity identity)
    {
        return GetUserId(identity) ?? throw new InvalidOperationException("User id is not found.");
    }

    public static AccountId? GetAccountId(this ClaimsPrincipal? principal)
    {
        var id = principal?.FindFirst(FrameworkClaimTypes.AccountId)?.Value;

        if (id is null)
        {
            return null;
        }

        return AccountId.TryParse(id, CultureInfo.InvariantCulture, out var accountId) ? accountId : null;
    }

    public static AccountId GetRequiredAccountId(this ClaimsPrincipal? principal)
    {
        return GetAccountId(principal) ?? throw new InvalidOperationException("Account id is not found.");
    }

    public static AccountId? GetAccountId(this IIdentity identity)
    {
        var claimsIdentity = identity as ClaimsIdentity;
        var id = claimsIdentity?.FindFirst(FrameworkClaimTypes.AccountId)?.Value;

        if (id is null)
        {
            return null;
        }

        return AccountId.TryParse(id, CultureInfo.InvariantCulture, out var accountId) ? accountId : null;
    }

    public static AccountId GetRequiredAccountId(this IIdentity identity)
    {
        return GetAccountId(identity) ?? throw new InvalidOperationException("Account id is not found.");
    }

    public static string? GetEditionId(this ClaimsPrincipal? principal)
    {
        return principal?.FindFirst(FrameworkClaimTypes.EditionId)?.Value;
    }

    public static string? GetEditionId(this IIdentity identity)
    {
        var claimsIdentity = identity as ClaimsIdentity;
        return claimsIdentity?.FindFirst(FrameworkClaimTypes.EditionId)?.Value;
    }

    public static string? GetTenantId(this ClaimsPrincipal? principal)
    {
        return principal?.FindFirst(FrameworkClaimTypes.TenantId)?.Value;
    }

    public static string? GetTenantId(this IIdentity identity)
    {
        var claimsIdentity = identity as ClaimsIdentity;
        return claimsIdentity?.FindFirst(FrameworkClaimTypes.TenantId)?.Value;
    }

    public static string? GetAccountType(this ClaimsPrincipal? principal)
    {
        return principal?.FindFirst(FrameworkClaimTypes.AccountType)?.Value;
    }

    public static ImmutableHashSet<string> GetRoles(this ClaimsPrincipal principal)
    {
        Argument.IsNotNull(principal);

        var roles = principal
            .Claims.Where(claim => string.Equals(claim.Type, FrameworkClaimTypes.Roles, StringComparison.Ordinal))
            .Select(claim => claim.Value)
            .ToImmutableHashSet(StringComparer.Ordinal);

        return roles;
    }
}
