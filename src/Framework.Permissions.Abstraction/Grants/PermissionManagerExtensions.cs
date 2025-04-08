// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Framework.Abstractions;
using Framework.Permissions.Models;

namespace Framework.Permissions.Grants;

public static class PermissionManagerExtensions
{
    public static async Task<bool> IsGrantedAsync(
        this IPermissionManager permissionManager,
        ICurrentUser currentUser,
        string name,
        CancellationToken cancellationToken = default
    )
    {
        var result = await permissionManager.GetAsync(name, currentUser, cancellationToken: cancellationToken);

        return result.IsGranted;
    }

    public static async Task<MultiplePermissionGrantResult> IsGrantedAsync(
        this IPermissionManager permissionManager,
        ICurrentUser currentUser,
        IReadOnlyCollection<string> name,
        CancellationToken cancellationToken = default
    )
    {
        var grantResults = await permissionManager.GetAllAsync(name, currentUser, cancellationToken: cancellationToken);

        var result = new MultiplePermissionGrantResult();

        foreach (var grantResult in grantResults)
        {
            result.Add(grantResult.Name, grantResult.IsGranted);
        }

        return result;
    }

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
