// Copyright (c) Mahmoud Shaheen, 2024. All rights reserved

using System.Security.Claims;
using Framework.Kernel.Primitives;

namespace Framework.Kernel.BuildingBlocks.Abstractions;

public interface ICurrentUser
{
    bool IsAuthenticated { get; }

    UserId? UserId { get; }

    string? AccountType { get; }

    AccountId? AccountId { get; }

    IReadOnlySet<string> Roles { get; }

    Claim? FindClaim(string claimType);

    Claim[] FindClaims(string claimType);

    Claim[] GetAllClaims();
}

public sealed class NullCurrentUser : ICurrentUser
{
    public bool IsAuthenticated => false;

    public UserId? UserId => null;

    public string? AccountType => null;

    public AccountId? AccountId => null;

    public IReadOnlySet<string> Roles => ImmutableHashSet<string>.Empty;

    public Claim? FindClaim(string claimType) => null;

    public Claim[] FindClaims(string claimType) => [];

    public Claim[] GetAllClaims() => [];
}

public sealed class PrincipalCurrentUser(ClaimsPrincipal principal) : ICurrentUser
{
    public bool IsAuthenticated => UserId is not null;

    public UserId? UserId => principal.GetUserId();

    public string? AccountType => principal.GetAccountType();

    public AccountId? AccountId => principal.GetAccountId();

    public IReadOnlySet<string> Roles => principal.GetRoles();

    public Claim? FindClaim(string claimType)
    {
        return principal.Claims.FirstOrDefault(c => string.Equals(c.Type, claimType, StringComparison.Ordinal));
    }

    public Claim[] FindClaims(string claimType)
    {
        return principal.Claims.Where(c => string.Equals(c.Type, claimType, StringComparison.Ordinal)).ToArray();
    }

    public Claim[] GetAllClaims()
    {
        return principal.Claims.ToArray();
    }
}
