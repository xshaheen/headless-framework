using Framework.Permissions.Entities;

namespace Framework.Permissions.Values;

public interface IPermissionGrantRepository
{
    Task<PermissionGrantRecord?> FindAsync(
        string name,
        string providerName,
        string providerKey,
        CancellationToken cancellationToken = default
    );

    Task<List<PermissionGrantRecord>> GetListAsync(
        string providerName,
        string providerKey,
        CancellationToken cancellationToken = default
    );

    Task<List<PermissionGrantRecord>> GetListAsync(
        string[] names,
        string providerName,
        string providerKey,
        CancellationToken cancellationToken = default
    );

    Task InsertAsync(PermissionGrantRecord permissionGrant);

    Task DeleteAsync(PermissionGrantRecord permissionGrant);

    Task UpdateAsync(PermissionGrantRecord permissionGrant);

    Task InsertManyAsync(IEnumerable<PermissionGrantRecord> permissionGrants);
}
