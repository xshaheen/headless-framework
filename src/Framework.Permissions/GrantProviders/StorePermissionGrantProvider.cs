// Copyright (c) Mahmoud Shaheen, 2024. All rights reserved

using Framework.Kernel.BuildingBlocks.Abstractions;
using Framework.Permissions.Grants;
using Framework.Permissions.Models;
using Framework.Permissions.Results;

namespace Framework.Permissions.GrantProviders;

public abstract class StorePermissionGrantProvider(IPermissionGrantStore grantStore, ICurrentTenant currentTenant)
    : IPermissionGrantProvider
{
    public abstract string Name { get; }

    public async Task<PermissionGrantResult> CheckAsync(
        PermissionDefinition permission,
        ICurrentUser currentUser,
        CancellationToken cancellationToken = default
    )
    {
        var result = await CheckAsync([permission], currentUser, cancellationToken);

        return result.First().Value;
    }

    public abstract Task<MultiplePermissionGrantStatusResult> CheckAsync(
        IReadOnlyCollection<PermissionDefinition> permissions,
        ICurrentUser currentUser,
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
            ? grantStore.GrantAsync(permission.Name, Name, providerKey, currentTenant.Id, cancellationToken)
            : grantStore.RevokeAsync(permission.Name, Name, providerKey, cancellationToken);
    }

    public Task SetAsync(
        IReadOnlyCollection<PermissionDefinition> permissions,
        string providerKey,
        bool isGranted,
        CancellationToken cancellationToken = default
    )
    {
        var names = permissions.Select(x => x.Name).ToArray();

        return isGranted
            ? grantStore.GrantAsync(names, Name, providerKey, currentTenant.Id, cancellationToken)
            : grantStore.RevokeAsync(names, Name, providerKey, cancellationToken);
    }
}
