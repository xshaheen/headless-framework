// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Permissions.Entities;

namespace Headless.Permissions.Repositories;

/// <summary>
/// Storage contract for persisting and querying permission grant records. Implemented by the EF Core /
/// PostgreSQL / SQL Server provider packages.
/// </summary>
public interface IPermissionGrantRepository
{
    /// <summary>
    /// Finds a single grant record by the (name, providerName, providerKey) composite key.
    /// Returns <see langword="null"/> when no matching record exists.
    /// </summary>
    /// <param name="name">Permission name.</param>
    /// <param name="providerName">The grant provider name (e.g. <c>"User"</c> or <c>"Role"</c>).</param>
    /// <param name="providerKey">The provider-specific subject key (e.g. user id or role name).</param>
    Task<PermissionGrantRecord?> FindAsync(
        string name,
        string providerName,
        string providerKey,
        CancellationToken cancellationToken = default
    );

    /// <summary>
    /// Returns all grant records for a given provider/subject, regardless of permission name. Useful for loading
    /// the full grant set for a single principal.
    /// </summary>
    /// <param name="providerName">The grant provider type name.</param>
    /// <param name="providerKey">The provider-specific subject key.</param>
    Task<List<PermissionGrantRecord>> GetListAsync(
        string providerName,
        string providerKey,
        CancellationToken cancellationToken = default
    );

    /// <summary>
    /// Returns grant records for the specified permission <paramref name="names"/> scoped to a single
    /// provider/subject. Prefer this overload for batch permission checks to avoid N+1 queries.
    /// </summary>
    /// <param name="names">The permission names to look up. Must not be <see langword="null"/>.</param>
    /// <param name="providerName">The grant provider type name.</param>
    /// <param name="providerKey">The provider-specific subject key.</param>
    Task<List<PermissionGrantRecord>> GetListAsync(
        IReadOnlyCollection<string> names,
        string providerName,
        string providerKey,
        CancellationToken cancellationToken = default
    );

    /// <summary>Inserts a single new grant record.</summary>
    Task InsertAsync(PermissionGrantRecord permissionGrant, CancellationToken cancellationToken = default);

    /// <summary>Inserts multiple new grant records in a single batch.</summary>
    Task InsertManyAsync(
        IEnumerable<PermissionGrantRecord> permissionGrants,
        CancellationToken cancellationToken = default
    );

    /// <summary>Deletes a single grant record.</summary>
    Task DeleteAsync(PermissionGrantRecord permissionGrant, CancellationToken cancellationToken);

    /// <summary>Deletes multiple grant records in a single batch.</summary>
    Task DeleteManyAsync(
        IReadOnlyCollection<PermissionGrantRecord> permissionGrants,
        CancellationToken cancellationToken = default
    );
}
