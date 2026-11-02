// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;
using Headless.Context;
using Microsoft.AspNetCore.Authorization;

namespace Headless.Permissions;

/// <summary>
/// ASP.NET Core authorization requirement that demands multiple named permissions. Handled by
/// <see cref="PermissionsRequirementHandler"/>. When <see cref="RequiresAll"/> is <see langword="true"/>
/// all listed permissions must be granted; when <see langword="false"/> at least one must be granted.
/// </summary>
[PublicAPI]
public sealed class PermissionsRequirement : IAuthorizationRequirement
{
    /// <summary>Initializes the requirement.</summary>
    /// <param name="permissionNames">
    /// The permission names to evaluate, at least one. Duplicates are dropped, keeping the first occurrence's order.
    /// </param>
    /// <param name="requiresAll">
    /// <see langword="true"/> to require every permission; <see langword="false"/> to require at least one.
    /// </param>
    /// <exception cref="ArgumentNullException"><paramref name="permissionNames"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException"><paramref name="permissionNames"/> is empty.</exception>
    public PermissionsRequirement(string[] permissionNames, bool requiresAll)
    {
        // An empty all-of set is vacuously satisfied and would authorize every caller, so it is refused here rather
        // than at request time.
        Argument.IsNotNullOrEmpty(permissionNames);

        // The batch grant lookup keys its result by name, so a repeated name would fail every request with a 500.
        PermissionNames = [.. permissionNames.Distinct(StringComparer.Ordinal)];
        RequiresAll = requiresAll;
    }

    /// <summary>The distinct permission names to evaluate.</summary>
    public string[] PermissionNames { get; }

    /// <summary>
    /// When <see langword="true"/>, all listed permissions must be granted (AND).
    /// When <see langword="false"/>, any single granted permission satisfies the requirement (OR).
    /// </summary>
    public bool RequiresAll { get; }

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
