// Copyright (c) Mahmoud Shaheen, 2024. All rights reserved

using Framework.Kernel.BuildingBlocks.Abstractions;
using Framework.Permissions.Results;

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
}
