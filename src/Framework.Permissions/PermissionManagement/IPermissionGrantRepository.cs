using Framework.Permissions.Entities;

namespace Framework.Permissions.PermissionManagement;

public interface IPermissionGrantRepository
{
    Task<PermissionGrant> FindAsync(
        string name,
        string providerName,
        string providerKey,
        CancellationToken cancellationToken = default
    );

    Task<List<PermissionGrant>> GetListAsync(
        string providerName,
        string providerKey,
        CancellationToken cancellationToken = default
    );

    Task<List<PermissionGrant>> GetListAsync(
        string[] names,
        string providerName,
        string providerKey,
        CancellationToken cancellationToken = default
    );

    Task InsertAsync(PermissionGrant permissionGrant);

    Task DeleteAsync(PermissionGrant permissionGrant);

    Task<PermissionGrant> UpdateAsync(PermissionGrant permissionGrant);
}
