// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Security.Principal;
using Headless.Checks;
using Headless.Constants;
using AccountId = Headless.Primitives.AccountId;
using UserId = Headless.Primitives.UserId;

namespace System.Security.Claims;

/// <summary>Extensions for reading well-known claims from an <see cref="IIdentity"/>.</summary>
[PublicAPI]
public static class HeadlessIdentityClaimsExtensions
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
