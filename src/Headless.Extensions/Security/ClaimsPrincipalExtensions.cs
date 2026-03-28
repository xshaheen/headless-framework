// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Security.Principal;
using Headless.Checks;
using Headless.Constants;
using AccountId = Headless.Primitives.AccountId;
using UserId = Headless.Primitives.UserId;

#pragma warning disable CA1708, IDE0130
// ReSharper disable once CheckNamespace
namespace System.Security.Claims;

[PublicAPI]
public static class ClaimsPrincipalExtensions
{
    public static bool IsSignedIn(this ClaimsPrincipal principal, string authenticationScheme)
    {
        Argument.IsNotNull(principal);

        return principal.Identities.Any(i =>
            string.Equals(i.AuthenticationType, authenticationScheme, StringComparison.Ordinal)
        );
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
        Argument.IsNotNull(claim);

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

        foreach (var x in claimsIdentity.FindAll(claimType).ToList())
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

    extension(IIdentity identity)
    {
        public UserId? GetUserId()
        {
            var id = (identity as ClaimsIdentity)?.FindFirst(UserClaimTypes.UserId)?.Value;

            if (id is null)
            {
                return null;
            }

            return UserId.TryParse(id, CultureInfo.InvariantCulture, out var userId) ? userId : null;
        }

        public UserId GetRequiredUserId()
        {
            return identity.GetUserId() ?? throw new InvalidOperationException("User id is not found.");
        }

        public AccountId? GetAccountId()
        {
            var id = (identity as ClaimsIdentity)?.FindFirst(UserClaimTypes.AccountId)?.Value;

            if (id is null)
            {
                return null;
            }

            return AccountId.TryParse(id, CultureInfo.InvariantCulture, out var accountId) ? accountId : null;
        }

        public AccountId GetRequiredAccountId()
        {
            return identity.GetAccountId() ?? throw new InvalidOperationException("Account id is not found.");
        }

        public string? GetEditionId()
        {
            return (identity as ClaimsIdentity)?.FindFirst(UserClaimTypes.EditionId)?.Value;
        }

        public string? GetSessionId()
        {
            return (identity as ClaimsIdentity)?.FindFirst(JwtClaimTypes.SessionId)?.Value;
        }

        public string? GetTenantId()
        {
            return (identity as ClaimsIdentity)?.FindFirst(UserClaimTypes.TenantId)?.Value;
        }
    }

    extension(ClaimsPrincipal? principal)
    {
        public string? GetAccountType()
        {
            return principal?.FindFirst(UserClaimTypes.AccountType)?.Value;
        }

        public ImmutableHashSet<string> GetRoles()
        {
            if (principal is null)
            {
                return [];
            }

            return principal
                .Claims.Where(claim => string.Equals(claim.Type, UserClaimTypes.Roles, StringComparison.Ordinal))
                .Select(claim => claim.Value)
                .ToImmutableHashSet(StringComparer.Ordinal);
        }

        public string? GetTenantId()
        {
            return principal?.FindFirst(UserClaimTypes.TenantId)?.Value;
        }

        public UserId? GetUserId()
        {
            var id = principal?.FindFirst(UserClaimTypes.UserId)?.Value;

            if (id is null)
            {
                return null;
            }

            return UserId.TryParse(id, CultureInfo.InvariantCulture, out var userId) ? userId : null;
        }

        public UserId GetRequiredUserId()
        {
            return principal.GetUserId() ?? throw new InvalidOperationException("User id is not found.");
        }

        public AccountId? GetAccountId()
        {
            var id = principal?.FindFirst(UserClaimTypes.AccountId)?.Value;

            if (id is null)
            {
                return null;
            }

            return AccountId.TryParse(id, CultureInfo.InvariantCulture, out var accountId) ? accountId : null;
        }

        public AccountId GetRequiredAccountId()
        {
            return principal.GetAccountId() ?? throw new InvalidOperationException("Account id is not found.");
        }

        public string? GetEditionId()
        {
            return principal?.FindFirst(UserClaimTypes.EditionId)?.Value;
        }

        public string? GetSessionId()
        {
            return principal?.FindFirst(JwtClaimTypes.SessionId)?.Value;
        }
    }
}
