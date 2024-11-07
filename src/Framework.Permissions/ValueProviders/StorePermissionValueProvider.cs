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
        string providerName,
        string providerKey,
        CancellationToken cancellationToken = default
    )
    {
        var result = await CheckAsync([permission], providerName, providerKey, cancellationToken);

        return result.Result.First().Value;
    }

    public async Task<MultiplePermissionGrantResult> CheckAsync(
        List<PermissionDefinition> permissions,
        string providerName,
        string providerKey,
        CancellationToken cancellationToken = default
    )
    {
        var permissionNames = permissions.ConvertAll(x => x.Name);

        if (!string.Equals(providerName, Name, StringComparison.Ordinal))
        {
            return new MultiplePermissionGrantResult(permissionNames);
        }

        return await permissionGrantStore.IsGrantedAsync(permissionNames, Name, providerKey, cancellationToken);
    }

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
