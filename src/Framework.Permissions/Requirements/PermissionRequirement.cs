// Copyright (c) Mahmoud Shaheen, 2024. All rights reserved

using Framework.Kernel.Checks;
using Framework.Permissions.Checkers;
using Microsoft.AspNetCore.Authorization;

namespace Framework.Permissions.Requirements;

[PublicAPI]
public sealed class PermissionRequirement(string permissionName) : IAuthorizationRequirement
{
    public string PermissionName { get; } = Argument.IsNotNull(permissionName);

    public override string ToString() => $"PermissionRequirement: {PermissionName}";
}

[PublicAPI]
public class PermissionRequirementHandler(IPermissionChecker checker) : AuthorizationHandler<PermissionRequirement>
{
    protected override async Task HandleRequirementAsync(
        AuthorizationHandlerContext context,
        PermissionRequirement requirement
    )
    {
        if (await checker.IsGrantedAsync(context.User, requirement.PermissionName))
        {
            context.Succeed(requirement);
        }
    }
}
