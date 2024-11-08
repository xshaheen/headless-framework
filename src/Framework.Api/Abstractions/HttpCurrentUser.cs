// Copyright (c) Mahmoud Shaheen, 2024. All rights reserved

using System.Security.Claims;
using Framework.Kernel.BuildingBlocks.Abstractions;
using Framework.Kernel.Primitives;

namespace Framework.Api.Abstractions;

public sealed class HttpCurrentUser(ICurrentPrincipalAccessor accessor) : ICurrentUser
{
    public bool IsAuthenticated => UserId is not null;

    public UserId? UserId => accessor.Principal.GetUserId();

    public string? AccountType => accessor.Principal.GetAccountType();

    public AccountId? AccountId => accessor.Principal.GetAccountId();

    public IReadOnlySet<string> Roles => accessor.Principal.GetRoles();

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
