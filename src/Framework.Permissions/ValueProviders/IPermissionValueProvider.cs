// Copyright (c) Mahmoud Shaheen, 2024. All rights reserved

using System.Security.Claims;
using Framework.Kernel.BuildingBlocks.Abstractions;
using Framework.Kernel.Checks;
using Framework.Permissions.Entities;
using Framework.Permissions.Models;
using Framework.Permissions.Results;
using Framework.Permissions.Values;

namespace Framework.Permissions.ValueProviders;

public interface IPermissionValueProvider
{
    string Name { get; }

    //

    Task<PermissionGrantResult> GetResultAsync(PermissionDefinition permission, string providerKey);

    Task<MultiplePermissionGrantResult> GetResultAsync(List<PermissionDefinition> permissions, string providerKey);

    //

    Task<PermissionValueProviderGrantInfo> CheckAsync(string name, string providerName, string providerKey);

    Task<MultiplePermissionValueProviderGrantInfo> CheckAsync(string[] names, string providerName, string providerKey);

    Task SetAsync(string name, string providerKey, bool isGranted);
}

public abstract class StorePermissionValueProvider(IPermissionValueStore store) : IPermissionValueProvider
{
    public abstract string Name { get; }

    protected IPermissionValueStore PermissionValueStore { get; } = store;

    public abstract Task<PermissionGrantResult> GetResultAsync(
        PermissionDefinition permission,
        ClaimsPrincipal? principal
    );

    public abstract Task<MultiplePermissionGrantResult> GetResultAsync(
        List<PermissionDefinition> permissions,
        ClaimsPrincipal? principal
    );
}

public abstract class StorePermissionManagementProvider(
    IPermissionGrantRepository repository,
    IGuidGenerator guidGenerator,
    ICurrentTenant currentTenant
) : IPermissionManagementProvider
{
    public abstract string Name { get; }

    public virtual async Task<PermissionValueProviderGrantInfo> CheckAsync(
        string name,
        string providerName,
        string providerKey
    )
    {
        var multiplePermissionValueProviderGrantInfo = await CheckAsync([name], providerName, providerKey);

        return multiplePermissionValueProviderGrantInfo.Result.First().Value;
    }

    public virtual async Task<MultiplePermissionValueProviderGrantInfo> CheckAsync(
        string[] names,
        string providerName,
        string providerKey
    )
    {
        var multiplePermissionValueProviderGrantInfo = new MultiplePermissionValueProviderGrantInfo(names);

        if (!string.Equals(providerName, Name, StringComparison.Ordinal))
        {
            return multiplePermissionValueProviderGrantInfo;
        }

        var permissionGrants = await repository.GetListAsync(names, providerName, providerKey);

        foreach (var permissionName in names)
        {
            var isGrant = permissionGrants.Exists(x => string.Equals(x.Name, permissionName, StringComparison.Ordinal));

            multiplePermissionValueProviderGrantInfo.Result[permissionName] = new(isGrant, providerKey);
        }

        return multiplePermissionValueProviderGrantInfo;
    }

    public virtual Task SetAsync(string name, string providerKey, bool isGranted)
    {
        return isGranted ? _GrantAsync(name, providerKey) : _RevokeAsync(name, providerKey);
    }

    private async Task _GrantAsync(string name, string providerKey)
    {
        var permissionGrant = await repository.FindAsync(name, Name, providerKey);
        if (permissionGrant != null)
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

public sealed class UserPermissionManagementProvider(
    IPermissionGrantRepository permissionGrantRepository,
    IGuidGenerator guidGenerator,
    ICurrentTenant currentTenant
) : StorePermissionManagementProvider(permissionGrantRepository, guidGenerator, currentTenant)
{
    public override string Name => UserPermissionValueProvider.ProviderName;
}

public interface IUserRoleFinder
{
    Task<string[]> GetRoleNamesAsync(Guid userId);
}

public sealed class RolePermissionManagementProvider(
    IPermissionGrantRepository permissionGrantRepository,
    IGuidGenerator guidGenerator,
    ICurrentTenant currentTenant,
    IUserRoleFinder userRoleFinder
) : StorePermissionManagementProvider(permissionGrantRepository, guidGenerator, currentTenant)
{
    private readonly IPermissionGrantRepository _permissionGrantRepository = permissionGrantRepository;

    public override string Name => RolePermissionValueProvider.ProviderName;

    public override async Task<PermissionValueProviderGrantInfo> CheckAsync(
        string name,
        string providerName,
        string providerKey
    )
    {
        var multipleGrantInfo = await CheckAsync([name], providerName, providerKey);

        return multipleGrantInfo.Result.Values.First();
    }

    public override async Task<MultiplePermissionValueProviderGrantInfo> CheckAsync(
        string[] names,
        string providerName,
        string providerKey
    )
    {
        var multiplePermissionValueProviderGrantInfo = new MultiplePermissionValueProviderGrantInfo(names);
        var permissionGrants = new List<PermissionGrantRecord>();

        if (string.Equals(providerName, Name, StringComparison.Ordinal))
        {
            permissionGrants.AddRange(await _permissionGrantRepository.GetListAsync(names, providerName, providerKey));
        }

        if (string.Equals(providerName, UserPermissionValueProvider.ProviderName, StringComparison.Ordinal))
        {
            var userId = Guid.Parse(providerKey);
            var roleNames = await userRoleFinder.GetRoleNamesAsync(userId);

            foreach (var roleName in roleNames)
            {
                permissionGrants.AddRange(await _permissionGrantRepository.GetListAsync(names, Name, roleName));
            }
        }

        permissionGrants = permissionGrants.Distinct().ToList();

        if (permissionGrants.Count == 0)
        {
            return multiplePermissionValueProviderGrantInfo;
        }

        foreach (var permissionName in names)
        {
            var permissionGrant = permissionGrants.Find(x =>
                string.Equals(x.Name, permissionName, StringComparison.Ordinal)
            );

            if (permissionGrant != null)
            {
                multiplePermissionValueProviderGrantInfo.Result[permissionName] = new(
                    isGranted: true,
                    permissionGrant.ProviderKey
                );
            }
        }

        return multiplePermissionValueProviderGrantInfo;
    }
}

#region Results

public sealed class MultiplePermissionValueProviderGrantInfo
{
    public Dictionary<string, PermissionValueProviderGrantInfo> Result { get; }

    public MultiplePermissionValueProviderGrantInfo()
    {
        Result = new(StringComparer.Ordinal);
    }

    public MultiplePermissionValueProviderGrantInfo(string[] names)
    {
        Argument.IsNotNull(names);

        Result = new(StringComparer.Ordinal);

        foreach (var name in names)
        {
            Result.Add(name, PermissionValueProviderGrantInfo.NonGranted);
        }
    }
}

#endregion
