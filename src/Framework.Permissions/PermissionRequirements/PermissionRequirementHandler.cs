// Copyright (c) Mahmoud Shaheen, 2024. All rights reserved

using Framework.Permissions.Permissions.Checkers;
using Microsoft.AspNetCore.Authorization;

namespace Framework.Permissions.PermissionRequirements;

public class PermissionRequirementHandler(IPermissionChecker permissionChecker)
    : AuthorizationHandler<PermissionRequirement>
{
    protected override async Task HandleRequirementAsync(
        AuthorizationHandlerContext context,
        PermissionRequirement requirement
    )
    {
        if (await permissionChecker.IsGrantedAsync(context.User, requirement.PermissionName))
        {
            context.Succeed(requirement);
        }
    }
}
