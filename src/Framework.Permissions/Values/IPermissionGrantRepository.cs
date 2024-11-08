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

    Task InsertAsync(PermissionGrantRecord permissionGrant, CancellationToken cancellationToken = default);

    Task UpdateAsync(PermissionGrantRecord permissionGrant, CancellationToken cancellationToken = default);

    Task DeleteAsync(PermissionGrantRecord permissionGrant, CancellationToken cancellationToken);

    Task InsertManyAsync(
        IEnumerable<PermissionGrantRecord> permissionGrants,
        CancellationToken cancellationToken = default
    );

    Task DeleteManyAsync(
        IEnumerable<PermissionGrantRecord> permissionGrants,
        CancellationToken cancellationToken = default
    );
}
