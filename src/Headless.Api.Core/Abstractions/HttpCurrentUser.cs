// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Security.Claims;
using Headless.Abstractions;
using Headless.Primitives;

namespace Headless.Api.Abstractions;

internal sealed class HttpCurrentUser(ICurrentPrincipalAccessor accessor) : ICurrentUser
{
    public bool IsAuthenticated => Principal?.Identity?.IsAuthenticated == true;

    public ClaimsPrincipal? Principal => accessor.Principal;

    public UserId? UserId => IsAuthenticated ? Principal.GetUserId() : null;

    public string? AccountType => IsAuthenticated ? Principal.GetAccountType() : null;

    public AccountId? AccountId => IsAuthenticated ? Principal.GetAccountId() : null;

    public IReadOnlySet<string> Roles => IsAuthenticated ? Principal.GetRoles() : [];
}
