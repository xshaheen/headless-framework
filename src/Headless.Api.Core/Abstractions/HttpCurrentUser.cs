// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Security.Claims;
using Headless.Abstractions;
using AccountId = Headless.Primitives.AccountId;
using UserId = Headless.Primitives.UserId;

namespace Headless.Api.Abstractions;

internal sealed class HttpCurrentUser(ICurrentPrincipalAccessor accessor) : ICurrentUser
{
    public bool IsAuthenticated => Principal?.Identity?.IsAuthenticated == true;

    public ClaimsPrincipal? Principal => accessor.Principal;

    public UserId? UserId => Principal.GetUserId();

    public string? AccountType => Principal.GetAccountType();

    public AccountId? AccountId => Principal.GetAccountId();

    public IReadOnlySet<string> Roles => Principal.GetRoles();
}
