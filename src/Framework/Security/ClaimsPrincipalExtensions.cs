// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Framework.Checks;

#pragma warning disable IDE0130
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
