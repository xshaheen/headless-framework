// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Security.Claims;
using Framework.Abstractions;
using Framework.Primitives;

namespace Framework.Testing.Helpers;

public sealed class TestCurrentUser : ICurrentUser
{
    public ClaimsPrincipal Principal { get; set; } = new();

    public bool IsAuthenticated { get; set; }

    public UserId? UserId { get; set; }

    public string? AccountType { get; set; }

    public AccountId? AccountId { get; set; }

    public IReadOnlySet<string> Roles => WritableRoles;

    public HashSet<string> WritableRoles { get; } = new(StringComparer.Ordinal);

    public Claim? FindClaim(string claimType)
    {
        return Principal.Claims.FirstOrDefault(c => string.Equals(c.Type, claimType, StringComparison.Ordinal));
    }

    public Claim[] FindClaims(string claimType)
    {
        return Principal.Claims.Where(c => string.Equals(c.Type, claimType, StringComparison.Ordinal)).ToArray();
    }
}
