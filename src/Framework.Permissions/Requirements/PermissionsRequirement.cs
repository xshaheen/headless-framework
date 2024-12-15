// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Framework.BuildingBlocks.Abstractions;
using Framework.Checks;
using Framework.Permissions.Grants;
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
public sealed class PermissionsRequirementHandler(IPermissionManager permissionManager)
    : AuthorizationHandler<PermissionsRequirement>
{
    protected override async Task HandleRequirementAsync(
        AuthorizationHandlerContext context,
        PermissionsRequirement requirement
    )
    {
        var multiplePermissionGrantResult = await permissionManager.IsGrantedAsync(
            new PrincipalCurrentUser(context.User),
            requirement.PermissionNames
        );

        if (
            requirement.RequiresAll
                ? multiplePermissionGrantResult.AllGranted
                : multiplePermissionGrantResult.Any(x => x.Value)
        )
        {
            context.Succeed(requirement);
        }
    }
}
