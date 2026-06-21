// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Abstractions;
using Headless.Permissions.Models;

namespace Headless.Permissions.Grants;

/// <summary>
/// Convenience helpers over <see cref="IPermissionManager"/>: boolean grant checks and the common
/// User/Role grant operations. The <c>Set</c>/<c>Grant</c>/<c>Revoke</c> helpers delegate to
/// <see cref="IPermissionManager.SetAsync(string, string, string, bool, CancellationToken)"/> and therefore
/// surface its <see cref="Headless.Exceptions.ConflictException"/> for undefined or disabled permissions and
/// unknown providers.
/// </summary>
public static class PermissionManagerExtensions
{
    /// <summary>Shorthand for resolving a single permission and returning its <see cref="GrantedPermissionResult.IsGranted"/> flag.</summary>
    /// <returns><see langword="true"/> if the permission is effectively granted; an undefined or disabled permission returns <see langword="false"/>.</returns>
    public static async Task<bool> IsGrantedAsync(
        this IPermissionManager permissionManager,
        ICurrentUser currentUser,
        string name,
        CancellationToken cancellationToken = default
    )
    {
        var result = await permissionManager
            .GetAsync(name, currentUser, cancellationToken: cancellationToken)
            .ConfigureAwait(false);

        return result.IsGranted;
    }

    /// <summary>Resolves several permissions at once and projects the results into a name-to-granted map.</summary>
    public static async Task<MultiplePermissionGrantResult> IsGrantedAsync(
        this IPermissionManager permissionManager,
        ICurrentUser currentUser,
        IReadOnlyCollection<string> name,
        CancellationToken cancellationToken = default
    )
    {
        var grantResults = await permissionManager
            .GetAllAsync(name, currentUser, cancellationToken: cancellationToken)
            .ConfigureAwait(false);

        var result = new MultiplePermissionGrantResult();

        foreach (var grantResult in grantResults)
        {
            result.Add(grantResult.Name, grantResult.IsGranted);
        }

        return result;
    }

    /// <summary>Grants or prohibits a permission for a specific user (sets the <see cref="PermissionGrantProviderNames.User"/> grant).</summary>
    public static Task SetToUserAsync(
        this IPermissionManager permissionManager,
        string permissionName,
        string userId,
        bool isGranted,
        CancellationToken cancellationToken = default
    )
    {
        return permissionManager.SetAsync(
            permissionName,
            PermissionGrantProviderNames.User,
            userId,
            isGranted,
            cancellationToken
        );
    }

    /// <summary>Grants a permission to a specific user.</summary>
    public static Task GrantToUserAsync(
        this IPermissionManager permissionManager,
        string permissionName,
        string userId,
        CancellationToken cancellationToken = default
    )
    {
        return permissionManager.SetAsync(
            permissionName,
            PermissionGrantProviderNames.User,
            userId,
            isGranted: true,
            cancellationToken
        );
    }

    /// <summary>
    /// Explicitly prohibits a permission for a specific user. This writes a denial (not just the absence of a
    /// grant), which overrides grants from other providers during resolution.
    /// </summary>
    public static Task RevokeFromUserAsync(
        this IPermissionManager permissionManager,
        string permissionName,
        string userId,
        CancellationToken cancellationToken = default
    )
    {
        return permissionManager.SetAsync(
            permissionName,
            PermissionGrantProviderNames.User,
            userId,
            isGranted: false,
            cancellationToken
        );
    }

    /// <summary>Grants or prohibits a permission for a specific role (sets the <see cref="PermissionGrantProviderNames.Role"/> grant).</summary>
    public static Task SetToRoleAsync(
        this IPermissionManager permissionManager,
        string permissionName,
        string roleName,
        bool isGranted,
        CancellationToken cancellationToken = default
    )
    {
        return permissionManager.SetAsync(
            permissionName,
            PermissionGrantProviderNames.Role,
            roleName,
            isGranted,
            cancellationToken
        );
    }

    /// <summary>Grants a permission to a specific role.</summary>
    public static Task GrantToRoleAsync(
        this IPermissionManager permissionManager,
        string permissionName,
        string roleName,
        CancellationToken cancellationToken = default
    )
    {
        return permissionManager.SetAsync(
            permissionName,
            PermissionGrantProviderNames.Role,
            roleName,
            isGranted: true,
            cancellationToken
        );
    }

    /// <summary>
    /// Explicitly prohibits a permission for a specific role. This writes a denial that overrides grants from
    /// other providers during resolution.
    /// </summary>
    public static Task RevokeFromRoleAsync(
        this IPermissionManager permissionManager,
        string permissionName,
        string roleName,
        CancellationToken cancellationToken = default
    )
    {
        return permissionManager.SetAsync(
            permissionName,
            PermissionGrantProviderNames.Role,
            roleName,
            isGranted: false,
            cancellationToken
        );
    }
}
