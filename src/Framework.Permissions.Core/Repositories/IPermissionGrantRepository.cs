// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Framework.Permissions.Entities;

namespace Framework.Permissions.Repositories;

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
        IReadOnlyCollection<string> names,
        string providerName,
        string providerKey,
        CancellationToken cancellationToken = default
    );

    Task InsertAsync(PermissionGrantRecord permissionGrant, CancellationToken cancellationToken = default);

    Task InsertManyAsync(
        IEnumerable<PermissionGrantRecord> permissionGrants,
        CancellationToken cancellationToken = default
    );

    Task DeleteAsync(PermissionGrantRecord permissionGrant, CancellationToken cancellationToken);

    Task DeleteManyAsync(
        IReadOnlyCollection<PermissionGrantRecord> permissionGrants,
        CancellationToken cancellationToken = default
    );
}
