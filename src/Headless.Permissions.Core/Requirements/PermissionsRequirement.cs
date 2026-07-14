// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Abstractions;
using Headless.Checks;
using Headless.Permissions.Grants;
using Microsoft.AspNetCore.Authorization;

namespace Headless.Permissions.Requirements;

/// <summary>
/// ASP.NET Core authorization requirement that demands multiple named permissions. Handled by
/// <see cref="PermissionsRequirementHandler"/>. When <see cref="RequiresAll"/> is <see langword="true"/>
/// all listed permissions must be granted; when <see langword="false"/> at least one must be granted.
/// </summary>
[PublicAPI]
public sealed class PermissionsRequirement(string[] permissionNames, bool requiresAll) : IAuthorizationRequirement
{
    /// <summary>The permission names to evaluate.</summary>
    public string[] PermissionNames { get; } = Argument.IsNotNull(permissionNames);

    /// <summary>
    /// When <see langword="true"/>, all listed permissions must be granted (AND).
    /// When <see langword="false"/>, any single granted permission satisfies the requirement (OR).
    /// </summary>
    public bool RequiresAll { get; } = requiresAll;

    public override string ToString()
    {
        return $"PermissionsRequirement: {string.Join(", ", PermissionNames)}";
    }
}

/// <summary>Handles <see cref="PermissionsRequirement"/> by delegating to <see cref="IPermissionManager"/>.</summary>
[PublicAPI]
public sealed class PermissionsRequirementHandler(IPermissionManager permissionManager)
    : AuthorizationHandler<PermissionsRequirement>
{
    protected override async Task HandleRequirementAsync(
        AuthorizationHandlerContext context,
        PermissionsRequirement requirement
    )
    {
        var multiplePermissionGrantResult = await permissionManager
            .IsGrantedAsync(new PrincipalCurrentUser(context.User), requirement.PermissionNames)
            .ConfigureAwait(false);

        if (
            requirement.RequiresAll
                ? multiplePermissionGrantResult.AllGranted
                : multiplePermissionGrantResult.Grants.Any(x => x.Value)
        )
        {
            context.Succeed(requirement);
        }
    }
}
