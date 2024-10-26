// Copyright (c) Mahmoud Shaheen, 2024. All rights reserved

using Framework.Kernel.Checks;
using Framework.Permissions.Checkers;
using Framework.Permissions.Models;
using Microsoft.AspNetCore.Authorization;

namespace Framework.Permissions.Requirements;

[PublicAPI]
public sealed class PermissionsRequirement(string[] permissionNames, bool requiresAll) : IAuthorizationRequirement
{
    public string[] PermissionNames { get; } = Argument.IsNotNull(permissionNames);

    public bool RequiresAll { get; } = requiresAll;

    public override string ToString()
    {
        return $"PermissionsRequirement: {string.Join(", ", PermissionNames)}";
    }
}

[PublicAPI]
public class PermissionsRequirementHandler(IPermissionChecker checker) : AuthorizationHandler<PermissionsRequirement>
{
    protected override async Task HandleRequirementAsync(
        AuthorizationHandlerContext context,
        PermissionsRequirement requirement
    )
    {
        var multiplePermissionGrantResult = await checker.IsGrantedAsync(context.User, requirement.PermissionNames);

        if (
            requirement.RequiresAll
                ? multiplePermissionGrantResult.AllGranted
                : multiplePermissionGrantResult.Result.Any(x => x.Value is PermissionGrantResult.Granted)
        )
        {
            context.Succeed(requirement);
        }
    }
}
