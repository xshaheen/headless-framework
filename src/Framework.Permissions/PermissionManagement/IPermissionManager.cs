using Framework.Permissions.Entities;
using Framework.Permissions.Models;

namespace Framework.Permissions.PermissionManagement;

//TODO: Write extension methods for simple IsGranted check

public interface IPermissionManager
{
    Task<PermissionWithGrantedProviders> GetAsync(string permissionName, string providerName, string providerKey);

    Task<MultiplePermissionWithGrantedProviders> GetAsync(
        string[] permissionNames,
        string provideName,
        string providerKey
    );

    Task<List<PermissionWithGrantedProviders>> GetAllAsync([NotNull] string providerName, [NotNull] string providerKey);

    Task SetAsync(string permissionName, string providerName, string providerKey, bool isGranted);

    Task<PermissionGrant> UpdateProviderKeyAsync(PermissionGrant permissionGrant, string providerKey);

    Task DeleteAsync(string providerName, string providerKey);
}
