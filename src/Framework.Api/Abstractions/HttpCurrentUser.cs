// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Security.Claims;
using Framework.Abstractions;
using Framework.Primitives;

namespace Framework.Api.Abstractions;

public sealed class HttpCurrentUser(ICurrentPrincipalAccessor accessor) : ICurrentUser
{
    public bool IsAuthenticated => UserId is not null;

    public ClaimsPrincipal? Principal => accessor.Principal;

    public UserId? UserId => Principal.GetUserId();

    public string? AccountType => Principal.GetAccountType();

    public AccountId? AccountId => Principal.GetAccountId();

    public IReadOnlySet<string> Roles => Principal.GetRoles();
}
