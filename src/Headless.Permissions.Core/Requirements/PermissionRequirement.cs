// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Abstractions;
using Headless.Checks;
using Headless.Permissions.Grants;
using Microsoft.AspNetCore.Authorization;

namespace Headless.Permissions.Requirements;

/// <summary>
/// ASP.NET Core authorization requirement that demands a single named permission. Add it to an authorization
/// policy; it is satisfied by <see cref="PermissionRequirementHandler"/>, which resolves the permission for the
/// current user through <see cref="IPermissionManager"/>.
/// </summary>
[PublicAPI]
public sealed class PermissionRequirement(string permissionName) : IAuthorizationRequirement
{
    /// <summary>The permission name that the current user must be granted.</summary>
    public string PermissionName { get; } = Argument.IsNotNull(permissionName);

    public override string ToString() => $"PermissionRequirement: {PermissionName}";
}

/// <summary>Handles <see cref="PermissionRequirement"/> by delegating to <see cref="IPermissionManager"/>.</summary>
[PublicAPI]
public sealed class PermissionRequirementHandler(IPermissionManager permissionManager)
    : AuthorizationHandler<PermissionRequirement>
{
    protected override async Task HandleRequirementAsync(
        AuthorizationHandlerContext context,
        PermissionRequirement requirement
    )
    {
        if (await permissionManager.IsGrantedAsync(new PrincipalCurrentUser(context.User), requirement.PermissionName))
        {
            context.Succeed(requirement);
        }
    }
}
