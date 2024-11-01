using Framework.Kernel.BuildingBlocks.Abstractions;
using Framework.Permissions.Entities;
using Framework.Permissions.Models;

namespace Framework.Permissions.PermissionManagement;

public abstract class PermissionManagementProvider(
    IPermissionGrantRepository permissionGrantRepository,
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

        var permissionGrants = await permissionGrantRepository.GetListAsync(names, providerName, providerKey);

        foreach (var permissionName in names)
        {
            var isGrant = permissionGrants.Exists(x => string.Equals(x.Name, permissionName, StringComparison.Ordinal));

            multiplePermissionValueProviderGrantInfo.Result[permissionName] = new(isGrant, providerKey);
        }

        return multiplePermissionValueProviderGrantInfo;
    }

    public virtual Task SetAsync(string name, string providerKey, bool isGranted)
    {
        return isGranted ? GrantAsync(name, providerKey) : RevokeAsync(name, providerKey);
    }

    protected virtual async Task GrantAsync(string name, string providerKey)
    {
        var permissionGrant = await permissionGrantRepository.FindAsync(name, Name, providerKey);
        if (permissionGrant != null)
        {
            return;
        }

        await permissionGrantRepository.InsertAsync(
            new PermissionGrant(guidGenerator.Create(), name, Name, providerKey, currentTenant.Id)
        );
    }

    protected virtual async Task RevokeAsync(string name, string providerKey)
    {
        var permissionGrant = await permissionGrantRepository.FindAsync(name, Name, providerKey);
        if (permissionGrant == null)
        {
            return;
        }

        await permissionGrantRepository.DeleteAsync(permissionGrant);
    }
}
