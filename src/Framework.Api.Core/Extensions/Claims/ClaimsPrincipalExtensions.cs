using Framework.Arguments;
using Framework.BuildingBlocks.Constants;
using Framework.BuildingBlocks.Primitives;

#pragma warning disable IDE0130
// ReSharper disable once CheckNamespace
namespace System.Security.Claims;

/// <summary>Provides a set of extension methods for operations on <see cref="ClaimsPrincipal"/>.</summary>
[PublicAPI]
public static class ClaimsPrincipalExtensions
{
    public static UserId? GetUserId(this ClaimsPrincipal? principal)
    {
        var id = principal?.FindFirst(PlatformClaimTypes.UserId)?.Value;

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

    public static AccountId? GetAccountId(this ClaimsPrincipal? principal)
    {
        var id = principal?.FindFirst(PlatformClaimTypes.AccountId)?.Value;

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

    public static string? GetUserType(this ClaimsPrincipal? principal)
    {
        return principal?.FindFirst(PlatformClaimTypes.AccountType)?.Value;
    }

    public static bool IsSignedIn(this ClaimsPrincipal principal, string authenticationScheme)
    {
        Argument.IsNotNull(principal);

        return principal.Identities.Any(i =>
            string.Equals(i.AuthenticationType, authenticationScheme, StringComparison.Ordinal)
        );
    }

    public static IReadOnlyList<string> GetRoles(this ClaimsPrincipal principal)
    {
        var roles = principal
            .Claims.Where(claim => string.Equals(claim.Type, PlatformClaimTypes.Roles, StringComparison.Ordinal))
            .Select(claim => claim.Value)
            .ToList();

        return roles;
    }

    public static ClaimsIdentity AddIfNotContains(this ClaimsIdentity claimsIdentity, Claim claim)
    {
        Argument.IsNotNull(claimsIdentity);

        if (!claimsIdentity.Claims.Any(x => string.Equals(x.Type, claim.Type, StringComparison.OrdinalIgnoreCase)))
        {
            claimsIdentity.AddClaim(claim);
        }

        return claimsIdentity;
    }

    public static ClaimsIdentity AddOrReplace(this ClaimsIdentity claimsIdentity, Claim claim)
    {
        Argument.IsNotNull(claimsIdentity);

        foreach (var x in claimsIdentity.FindAll(claim.Type).ToList())
        {
            claimsIdentity.RemoveClaim(x);
        }

        claimsIdentity.AddClaim(claim);

        return claimsIdentity;
    }

    public static ClaimsIdentity RemoveAll(this ClaimsIdentity claimsIdentity, string claimType)
    {
        Argument.IsNotNull(claimsIdentity);

        foreach (var x in claimsIdentity.FindAll(claimType))
        {
            claimsIdentity.RemoveClaim(x);
        }

        return claimsIdentity;
    }

    public static ClaimsPrincipal AddIdentityIfNotContains(this ClaimsPrincipal principal, ClaimsIdentity identity)
    {
        Argument.IsNotNull(principal);

        if (
            !principal.Identities.Any(x =>
                string.Equals(x.AuthenticationType, identity.AuthenticationType, StringComparison.OrdinalIgnoreCase)
            )
        )
        {
            principal.AddIdentity(identity);
        }

        return principal;
    }
}
