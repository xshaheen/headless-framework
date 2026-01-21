// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Security.Principal;
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
        var id = principal?.FindFirst(UserClaimTypes.UserId)?.Value;

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
        var id = (identity as ClaimsIdentity)?.FindFirst(UserClaimTypes.UserId)?.Value;

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
        var id = principal?.FindFirst(UserClaimTypes.AccountId)?.Value;

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
        var id = (identity as ClaimsIdentity)?.FindFirst(UserClaimTypes.AccountId)?.Value;

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
        return principal?.FindFirst(UserClaimTypes.EditionId)?.Value;
    }

    public static string? GetEditionId(this IIdentity identity)
    {
        return (identity as ClaimsIdentity)?.FindFirst(UserClaimTypes.EditionId)?.Value;
    }

    public static string? GetSessionId(this ClaimsPrincipal? principal)
    {
        return principal?.FindFirst(JwtClaimTypes.SessionId)?.Value;
    }

    public static string? GetSessionId(this IIdentity identity)
    {
        return (identity as ClaimsIdentity)?.FindFirst(JwtClaimTypes.SessionId)?.Value;
    }

    public static string? GetTenantId(this ClaimsPrincipal? principal)
    {
        return principal?.FindFirst(UserClaimTypes.TenantId)?.Value;
    }

    public static string? GetTenantId(this IIdentity identity)
    {
        return (identity as ClaimsIdentity)?.FindFirst(UserClaimTypes.TenantId)?.Value;
    }

    public static string? GetAccountType(this ClaimsPrincipal? principal)
    {
        return principal?.FindFirst(UserClaimTypes.AccountType)?.Value;
    }

    public static ImmutableHashSet<string> GetRoles(this ClaimsPrincipal? principal)
    {
        var roles = principal
            ?.Claims.Where(claim => string.Equals(claim.Type, UserClaimTypes.Roles, StringComparison.Ordinal))
            .Select(claim => claim.Value)
            .ToImmutableHashSet(StringComparer.Ordinal);

        return roles ?? [];
    }
}
