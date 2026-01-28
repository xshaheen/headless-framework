// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Security.Claims;
using Headless.Abstractions;
using Microsoft.AspNetCore.Authorization;

namespace Headless.Permissions.Testing;

public sealed class AlwaysAllowAuthorizationService(ICurrentPrincipalAccessor principalAccessor) : IAuthorizationService
{
    public ClaimsPrincipal? CurrentPrincipal => principalAccessor.Principal;

    public Task<AuthorizationResult> AuthorizeAsync(
        ClaimsPrincipal user,
        object? resource,
        IEnumerable<IAuthorizationRequirement> requirements
    )
    {
        return Task.FromResult(AuthorizationResult.Success());
    }

    public Task<AuthorizationResult> AuthorizeAsync(ClaimsPrincipal user, object? resource, string policyName)
    {
        return Task.FromResult(AuthorizationResult.Success());
    }
}
