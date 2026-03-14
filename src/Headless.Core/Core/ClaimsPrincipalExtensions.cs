// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Runtime.CompilerServices;
using System.Security.Principal;
using Headless.Constants;
using AccountId = Headless.Primitives.AccountId;
using UserId = Headless.Primitives.UserId;

#pragma warning disable CA1708, IDE0130
// ReSharper disable once CheckNamespace
namespace System.Security.Claims;

[PublicAPI]
public static class ClaimsPrincipalExtensions
{
    private static readonly ConditionalWeakTable<ClaimsPrincipal, ImmutableHashSet<string>> _RolesCache = [];

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

            return _RolesCache.GetValue(
                principal,
                static p =>
                    p.Claims.Where(claim => string.Equals(claim.Type, UserClaimTypes.Roles, StringComparison.Ordinal))
                        .Select(claim => claim.Value)
                        .ToImmutableHashSet(StringComparer.Ordinal)
            );
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
