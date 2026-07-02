// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Security.Principal;
using Headless.Checks;
using Headless.Constants;
using AccountId = Headless.Primitives.AccountId;
using UserId = Headless.Primitives.UserId;

#pragma warning disable IDE0130
// ReSharper disable once CheckNamespace
namespace System.Security.Claims;

/// <summary>Extensions for reading well-known claims and mutating identities on <see cref="ClaimsPrincipal"/> and <see cref="ClaimsIdentity"/>.</summary>
[PublicAPI]
public static class ClaimsPrincipalExtensions
{
    /// <summary>Determines whether <paramref name="principal"/> has an identity authenticated with the given scheme.</summary>
    /// <param name="principal">The principal to inspect.</param>
    /// <param name="authenticationScheme">The authentication scheme (identity <c>AuthenticationType</c>) to look for.</param>
    /// <returns><see langword="true"/> when an identity with a matching authentication type exists; otherwise <see langword="false"/>.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="principal"/> is <see langword="null"/>.</exception>
    public static bool IsSignedIn(this ClaimsPrincipal principal, string authenticationScheme)
    {
        Argument.IsNotNull(principal);

        return principal.Identities.Any(i =>
            string.Equals(i.AuthenticationType, authenticationScheme, StringComparison.Ordinal)
        );
    }

    /// <summary>Adds <paramref name="claim"/> to <paramref name="claimsIdentity"/> only if no claim of the same type already exists.</summary>
    /// <param name="claimsIdentity">The identity to add the claim to.</param>
    /// <param name="claim">The claim to add.</param>
    /// <returns>The same <paramref name="claimsIdentity"/> instance, for chaining.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="claimsIdentity"/> is <see langword="null"/>.</exception>
    public static ClaimsIdentity AddIfNotContains(this ClaimsIdentity claimsIdentity, Claim claim)
    {
        Argument.IsNotNull(claimsIdentity);

        if (!claimsIdentity.Claims.Any(x => string.Equals(x.Type, claim.Type, StringComparison.OrdinalIgnoreCase)))
        {
            claimsIdentity.AddClaim(claim);
        }

        return claimsIdentity;
    }

    /// <summary>Removes every existing claim of the same type as <paramref name="claim"/> and then adds <paramref name="claim"/>.</summary>
    /// <param name="claimsIdentity">The identity to update.</param>
    /// <param name="claim">The replacement claim.</param>
    /// <returns>The same <paramref name="claimsIdentity"/> instance, for chaining.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="claimsIdentity"/> or <paramref name="claim"/> is <see langword="null"/>.</exception>
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

    /// <summary>Removes all claims of the given <paramref name="claimType"/> from <paramref name="claimsIdentity"/>.</summary>
    /// <param name="claimsIdentity">The identity to update.</param>
    /// <param name="claimType">The claim type to remove.</param>
    /// <returns>The same <paramref name="claimsIdentity"/> instance, for chaining.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="claimsIdentity"/> is <see langword="null"/>.</exception>
    public static ClaimsIdentity RemoveAll(this ClaimsIdentity claimsIdentity, string claimType)
    {
        Argument.IsNotNull(claimsIdentity);

        foreach (var x in claimsIdentity.FindAll(claimType).ToList())
        {
            claimsIdentity.RemoveClaim(x);
        }

        return claimsIdentity;
    }

    /// <summary>Adds <paramref name="identity"/> to <paramref name="principal"/> only if no identity with the same authentication type already exists.</summary>
    /// <param name="principal">The principal to add the identity to.</param>
    /// <param name="identity">The identity to add.</param>
    /// <returns>The same <paramref name="principal"/> instance, for chaining.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="principal"/> is <see langword="null"/>.</exception>
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

    extension(ClaimsPrincipal? principal)
    {
        /// <summary>Reads the account type claim from <paramref name="principal"/>.</summary>
        /// <returns>The account type, or <see langword="null"/> when <paramref name="principal"/> or the claim is absent.</returns>
        public string? GetAccountType()
        {
            return principal?.FindFirst(UserClaimTypes.AccountType)?.Value;
        }

        /// <summary>Reads all role claims from <paramref name="principal"/>.</summary>
        /// <returns>The set of role values, or an empty set when <paramref name="principal"/> is <see langword="null"/> or has no role claims.</returns>
        public ImmutableHashSet<string> GetRoles()
        {
            if (principal is null)
            {
                return [];
            }

            return principal
                .Claims.Where(claim =>
                    string.Equals(claim.Type, UserClaimTypes.Roles, StringComparison.OrdinalIgnoreCase)
                )
                .Select(claim => claim.Value)
                .ToImmutableHashSet(StringComparer.Ordinal);
        }

        /// <summary>Reads the tenant id claim from <paramref name="principal"/>.</summary>
        /// <returns>The tenant id, or <see langword="null"/> when <paramref name="principal"/> or the claim is absent.</returns>
        public string? GetTenantId()
        {
            return principal?.FindFirst(UserClaimTypes.TenantId)?.Value;
        }

        /// <summary>Reads the user id claim from <paramref name="principal"/>, if present and parseable.</summary>
        /// <returns>The parsed <see cref="UserId"/>, or <see langword="null"/> when the claim is absent or cannot be parsed.</returns>
        public UserId? GetUserId()
        {
            var id = principal?.FindFirst(UserClaimTypes.UserId)?.Value;

            if (id is null)
            {
                return null;
            }

            return UserId.TryParse(id, CultureInfo.InvariantCulture, out var userId) ? userId : null;
        }

        /// <summary>Reads the user id claim from <paramref name="principal"/>, requiring it to be present and parseable.</summary>
        /// <returns>The parsed <see cref="UserId"/>.</returns>
        /// <exception cref="InvalidOperationException">Thrown when the user id claim is absent or cannot be parsed.</exception>
        public UserId GetRequiredUserId()
        {
            return principal.GetUserId() ?? throw new InvalidOperationException("User id is not found.");
        }

        /// <summary>Reads the account id claim from <paramref name="principal"/>, if present and parseable.</summary>
        /// <returns>The parsed <see cref="AccountId"/>, or <see langword="null"/> when the claim is absent or cannot be parsed.</returns>
        public AccountId? GetAccountId()
        {
            var id = principal?.FindFirst(UserClaimTypes.AccountId)?.Value;

            if (id is null)
            {
                return null;
            }

            return AccountId.TryParse(id, CultureInfo.InvariantCulture, out var accountId) ? accountId : null;
        }

        /// <summary>Reads the account id claim from <paramref name="principal"/>, requiring it to be present and parseable.</summary>
        /// <returns>The parsed <see cref="AccountId"/>.</returns>
        /// <exception cref="InvalidOperationException">Thrown when the account id claim is absent or cannot be parsed.</exception>
        public AccountId GetRequiredAccountId()
        {
            return principal.GetAccountId() ?? throw new InvalidOperationException("Account id is not found.");
        }

        /// <summary>Reads the edition id claim from <paramref name="principal"/>.</summary>
        /// <returns>The edition id, or <see langword="null"/> when <paramref name="principal"/> or the claim is absent.</returns>
        public string? GetEditionId()
        {
            return principal?.FindFirst(UserClaimTypes.EditionId)?.Value;
        }

        /// <summary>Reads the session id claim from <paramref name="principal"/>.</summary>
        /// <returns>The session id, or <see langword="null"/> when <paramref name="principal"/> or the claim is absent.</returns>
        public string? GetSessionId()
        {
            return principal?.FindFirst(JwtClaimTypes.SessionId)?.Value;
        }
    }
}

/// <summary>Extensions for reading well-known claims from an <see cref="IIdentity"/>.</summary>
[PublicAPI]
public static class IdentityClaimsExtensions
{
    extension(IIdentity identity)
    {
        /// <summary>Reads the user id claim from <paramref name="identity"/>, if present and parseable.</summary>
        /// <returns>The parsed <see cref="UserId"/>, or <see langword="null"/> when the claim is absent or cannot be parsed.</returns>
        public UserId? GetUserId()
        {
            var id = (identity as ClaimsIdentity)?.FindFirst(UserClaimTypes.UserId)?.Value;

            if (id is null)
            {
                return null;
            }

            return UserId.TryParse(id, CultureInfo.InvariantCulture, out var userId) ? userId : null;
        }

        /// <summary>Reads the user id claim from <paramref name="identity"/>, requiring it to be present and parseable.</summary>
        /// <returns>The parsed <see cref="UserId"/>.</returns>
        /// <exception cref="InvalidOperationException">Thrown when the user id claim is absent or cannot be parsed.</exception>
        public UserId GetRequiredUserId()
        {
            return identity.GetUserId() ?? throw new InvalidOperationException("User id is not found.");
        }

        /// <summary>Reads the account id claim from <paramref name="identity"/>, if present and parseable.</summary>
        /// <returns>The parsed <see cref="AccountId"/>, or <see langword="null"/> when the claim is absent or cannot be parsed.</returns>
        public AccountId? GetAccountId()
        {
            var id = (identity as ClaimsIdentity)?.FindFirst(UserClaimTypes.AccountId)?.Value;

            if (id is null)
            {
                return null;
            }

            return AccountId.TryParse(id, CultureInfo.InvariantCulture, out var accountId) ? accountId : null;
        }

        /// <summary>Reads the account id claim from <paramref name="identity"/>, requiring it to be present and parseable.</summary>
        /// <returns>The parsed <see cref="AccountId"/>.</returns>
        /// <exception cref="InvalidOperationException">Thrown when the account id claim is absent or cannot be parsed.</exception>
        public AccountId GetRequiredAccountId()
        {
            return identity.GetAccountId() ?? throw new InvalidOperationException("Account id is not found.");
        }

        /// <summary>Reads the edition id claim from <paramref name="identity"/>.</summary>
        /// <returns>The edition id, or <see langword="null"/> when the claim is absent.</returns>
        public string? GetEditionId()
        {
            return (identity as ClaimsIdentity)?.FindFirst(UserClaimTypes.EditionId)?.Value;
        }

        /// <summary>Reads the session id claim from <paramref name="identity"/>.</summary>
        /// <returns>The session id, or <see langword="null"/> when the claim is absent.</returns>
        public string? GetSessionId()
        {
            return (identity as ClaimsIdentity)?.FindFirst(JwtClaimTypes.SessionId)?.Value;
        }

        /// <summary>Reads the tenant id claim from <paramref name="identity"/>.</summary>
        /// <returns>The tenant id, or <see langword="null"/> when the claim is absent.</returns>
        public string? GetTenantId()
        {
            return (identity as ClaimsIdentity)?.FindFirst(UserClaimTypes.TenantId)?.Value;
        }
    }
}
