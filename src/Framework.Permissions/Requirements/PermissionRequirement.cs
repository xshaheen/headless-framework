// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Framework.Kernel.BuildingBlocks.Abstractions;
using Framework.Kernel.Checks;
using Framework.Permissions.Grants;
using Microsoft.AspNetCore.Authorization;

namespace Framework.Permissions.Requirements;

[PublicAPI]
public sealed class PermissionRequirement(string permissionName) : IAuthorizationRequirement
{
    public string PermissionName { get; } = Argument.IsNotNull(permissionName);

    public override string ToString() => $"PermissionRequirement: {PermissionName}";
}

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
