// Copyright (c) Mahmoud Shaheen, 2024. All rights reserved

using Framework.Permissions.Permissions.Checkers;
using Microsoft.AspNetCore.Authorization;

namespace Framework.Permissions.PermissionRequirements;

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
