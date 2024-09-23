// Copyright (c) Mahmoud Shaheen, 2024. All rights reserved

using System.Security.Claims;
using Framework.Api.Core.Security.Claims;
using Framework.Kernel.BuildingBlocks.Abstractions;
using Framework.Kernel.Primitives;

namespace Framework.Api.Core.Abstractions;

public sealed class HttpCurrentUser(ICurrentPrincipalAccessor accessor) : ICurrentUser
{
    public bool IsAuthenticated => UserId is not null;

    public UserId? UserId => accessor.Principal.GetUserId();

    public string? UserType => accessor.Principal.GetUserType();

    public AccountId? AccountId => accessor.Principal.GetAccountId();

    public IReadOnlyList<string> Roles => accessor.Principal.GetRoles();

    public Claim? FindClaim(string claimType)
    {
        return accessor.Principal.Claims.FirstOrDefault(c =>
            string.Equals(c.Type, claimType, StringComparison.Ordinal)
        );
    }

    public Claim[] FindClaims(string claimType)
    {
        return accessor
            .Principal.Claims.Where(c => string.Equals(c.Type, claimType, StringComparison.Ordinal))
            .ToArray();
    }

    public Claim[] GetAllClaims()
    {
        return accessor.Principal.Claims.ToArray();
    }
}
