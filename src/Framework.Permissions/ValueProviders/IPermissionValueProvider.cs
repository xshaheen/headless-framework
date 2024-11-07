// Copyright (c) Mahmoud Shaheen, 2024. All rights reserved

using Framework.Kernel.BuildingBlocks.Abstractions;
using Framework.Permissions.Entities;
using Framework.Permissions.Models;
using Framework.Permissions.Results;
using Framework.Permissions.Values;

namespace Framework.Permissions.ValueProviders;

public interface IPermissionValueProvider
{
    string Name { get; }

    Task<PermissionGrantResult> CheckAsync(
        PermissionDefinition permission,
        string providerName,
        string providerKey,
        CancellationToken cancellationToken = default
    );

    Task<MultiplePermissionGrantResult> CheckAsync(
        List<PermissionDefinition> permissions,
        string providerName,
        string providerKey,
        CancellationToken cancellationToken = default
    );

    Task SetAsync(
        PermissionDefinition permission,
        string providerKey,
        bool isGranted,
        CancellationToken cancellationToken = default
    );
}

public abstract class StorePermissionValueProvider(
    IPermissionGrantRepository repository,
    IPermissionGrantStore store,
    IGuidGenerator guidGenerator,
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

        return await store.IsGrantedAsync(permissionNames, Name, providerKey, cancellationToken);
    }

    public Task SetAsync(
        PermissionDefinition permission,
        string providerKey,
        bool isGranted,
        CancellationToken cancellationToken = default
    )
    {
        return isGranted ? _GrantAsync(permission.Name, providerKey) : _RevokeAsync(permission.Name, providerKey);
    }

    private async Task _GrantAsync(string name, string providerKey)
    {
        var permissionGrant = await repository.FindAsync(name, Name, providerKey);

        if (permissionGrant is not null)
        {
            return;
        }

        await repository.InsertAsync(
            new PermissionGrantRecord(guidGenerator.Create(), name, Name, providerKey, currentTenant.Id)
        );
    }

    private async Task _RevokeAsync(string name, string providerKey)
    {
        var permissionGrant = await repository.FindAsync(name, Name, providerKey);

        if (permissionGrant is null)
        {
            return;
        }

        await repository.DeleteAsync(permissionGrant);
    }
}
