// Copyright (c) Mahmoud Shaheen, 2024. All rights reserved

using System.Security.Claims;
using Framework.Kernel.Primitives;

namespace Framework.Kernel.BuildingBlocks.Abstractions;

public interface ICurrentUser
{
    bool IsAuthenticated { get; }

    UserId? UserId { get; }

    string? UserType { get; }

    AccountId? AccountId { get; }

    IReadOnlyList<string> Roles { get; }

    Claim? FindClaim(string claimType);

    Claim[] FindClaims(string claimType);

    Claim[] GetAllClaims();
}

public sealed class NullCurrentUser : ICurrentUser
{
    public bool IsAuthenticated => false;

    public UserId? UserId => null;

    public string? UserType => null;

    public AccountId? AccountId => null;

    public IReadOnlyList<string> Roles => Array.Empty<string>();

    public Claim? FindClaim(string claimType) => null;

    public Claim[] FindClaims(string claimType) => [];

    public Claim[] GetAllClaims() => [];
}
