// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;
using Headless.Context;
using Microsoft.AspNetCore.Authorization;

namespace Headless.Permissions;

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

    public override string ToString()
    {
        return $"PermissionRequirement: {PermissionName}";
    }
}

/// <summary>
/// Handles every <see cref="PermissionRequirement"/> and <see cref="PermissionsRequirement"/> in the evaluated policy
/// with one <see cref="IPermissionManager.GetAllAsync(IReadOnlyCollection{string}, ICurrentUser, string?, CancellationToken)"/>
/// read, then decides each requirement from that result.
/// </summary>
/// <remarks>
/// A route group, an endpoint, and a surface policy each add their own requirement to the combined policy; reading per
/// requirement would repeat the grant cache reads once for each. An undefined or disabled permission is never granted.
/// The handler never fails the evaluation, so a requirement it does not satisfy stays pending.
/// </remarks>
[PublicAPI]
public sealed class PermissionRequirementHandler(IPermissionManager permissionManager) : IAuthorizationHandler
{
    private readonly IPermissionManager _permissionManager = Argument.IsNotNull(permissionManager);

    public async Task HandleAsync(AuthorizationHandlerContext context)
    {
        Argument.IsNotNull(context);

        List<IAuthorizationRequirement>? requirements = null;
        HashSet<string>? names = null;

        foreach (var requirement in context.Requirements)
        {
            switch (requirement)
            {
                case PermissionRequirement single:
                    (requirements ??= []).Add(single);
                    (names ??= new(StringComparer.Ordinal)).Add(single.PermissionName);
                    break;
                case PermissionsRequirement multiple:
                    (requirements ??= []).Add(multiple);
                    (names ??= new(StringComparer.Ordinal)).UnionWith(multiple.PermissionNames);
                    break;
            }
        }

        if (requirements is null || names is null)
        {
            return;
        }

        // The handler contract carries no token; a grant read is a cache hit after the first request.
        var results = await _permissionManager
            .GetAllAsync(names, new PrincipalCurrentUser(context.User))
            .ConfigureAwait(false);

        var granted = new HashSet<string>(StringComparer.Ordinal);

        foreach (var result in results)
        {
            if (result.IsGranted)
            {
                granted.Add(result.Name);
            }
        }

        foreach (var requirement in requirements)
        {
            var isSatisfied = requirement switch
            {
                PermissionRequirement single => granted.Contains(single.PermissionName),
                PermissionsRequirement { RequiresAll: true } multiple => multiple.PermissionNames.All(granted.Contains),
                PermissionsRequirement multiple => multiple.PermissionNames.Any(granted.Contains),
                _ => false,
            };

            if (isSatisfied)
            {
                context.Succeed(requirement);
            }
        }
    }
}
