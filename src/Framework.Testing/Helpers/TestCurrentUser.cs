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
}
