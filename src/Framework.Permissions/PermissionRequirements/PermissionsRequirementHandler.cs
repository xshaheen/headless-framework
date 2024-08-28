using Framework.Permissions.Permissions.Checkers;
using Microsoft.AspNetCore.Authorization;

namespace Framework.Permissions.PermissionRequirements;

public class PermissionsRequirementHandler(IPermissionChecker permissionChecker)
    : AuthorizationHandler<PermissionsRequirement>
{
    protected override async Task HandleRequirementAsync(
        AuthorizationHandlerContext context,
        PermissionsRequirement requirement
    )
    {
        var multiplePermissionGrantResult = await permissionChecker.IsGrantedAsync(
            context.User,
            requirement.PermissionNames
        );

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
