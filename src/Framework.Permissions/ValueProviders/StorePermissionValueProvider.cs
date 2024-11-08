// Copyright (c) Mahmoud Shaheen, 2024. All rights reserved

using Framework.Kernel.BuildingBlocks.Abstractions;
using Framework.Permissions.Models;
using Framework.Permissions.Results;
using Framework.Permissions.Values;

namespace Framework.Permissions.ValueProviders;

public abstract class StorePermissionValueProvider(
    IPermissionGrantStore permissionGrantStore,
    ICurrentTenant currentTenant
) : IPermissionValueProvider
{
    public abstract string Name { get; }

    public async Task<PermissionGrantResult> CheckAsync(
        PermissionDefinition permission,
        ICurrentUser currentUser,
        string providerName,
        CancellationToken cancellationToken = default
    )
    {
        var result = await CheckAsync([permission], currentUser, providerName, cancellationToken);

        return result.Result.First().Value;
    }

    public abstract Task<MultiplePermissionGrantResult> CheckAsync(
        IReadOnlyCollection<PermissionDefinition> permissions,
        ICurrentUser currentUser,
        string providerName,
        CancellationToken cancellationToken = default
    );

    public Task SetAsync(
        PermissionDefinition permission,
        string providerKey,
        bool isGranted,
        CancellationToken cancellationToken = default
    )
    {
        return isGranted
            ? permissionGrantStore.GrantAsync(permission.Name, Name, providerKey, currentTenant.Id, cancellationToken)
            : permissionGrantStore.RevokeAsync(permission.Name, Name, providerKey, cancellationToken);
    }
}
