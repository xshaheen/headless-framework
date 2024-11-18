// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Security.Claims;
using Framework.BuildingBlocks.Abstractions;
using Framework.Primitives;

namespace Framework.Api.Abstractions;

public sealed class HttpCurrentUser(ICurrentPrincipalAccessor accessor) : ICurrentUser
{
    public bool IsAuthenticated => UserId is not null;

    public ClaimsPrincipal Principal => accessor.Principal;

    public UserId? UserId => Principal.GetUserId();

    public string? AccountType => Principal.GetAccountType();

    public AccountId? AccountId => Principal.GetAccountId();

    public IReadOnlySet<string> Roles => Principal.GetRoles();

    public Claim? FindClaim(string claimType)
    {
        return Principal.Claims.FirstOrDefault(c => string.Equals(c.Type, claimType, StringComparison.Ordinal));
    }

    public Claim[] FindClaims(string claimType)
    {
        return Principal.Claims.Where(c => string.Equals(c.Type, claimType, StringComparison.Ordinal)).ToArray();
    }
}
