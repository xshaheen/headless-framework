// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Permissions.Entities;

namespace Headless.Permissions.Repositories;

/// <summary>
/// Storage contract for persisting and querying the DB-side representation of permission group and permission
/// definitions. Implemented by the EF Core / PostgreSQL / SQL Server provider packages.
/// </summary>
public interface IPermissionDefinitionRecordRepository
{
    /// <summary>Returns all <see cref="PermissionDefinitionRecord"/> rows from the database.</summary>
    Task<List<PermissionDefinitionRecord>> GetPermissionsListAsync(CancellationToken cancellationToken = default);

    /// <summary>Returns all <see cref="PermissionGroupDefinitionRecord"/> rows from the database.</summary>
    Task<List<PermissionGroupDefinitionRecord>> GetGroupsListAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Applies a batch of creates, updates, and deletes for groups and permissions in a single atomic operation.
    /// The caller is responsible for computing which records belong in each list; the store does not recompute
    /// the diff.
    /// </summary>
    /// <param name="newGroups">Groups to insert.</param>
    /// <param name="updatedGroups">Groups to update (already patched by the caller).</param>
    /// <param name="deletedGroups">Groups to remove; associated permissions should be removed by cascade or the caller.</param>
    /// <param name="newPermissions">Permissions to insert.</param>
    /// <param name="updatedPermissions">Permissions to update (already patched by the caller).</param>
    /// <param name="deletedPermissions">Permissions to remove.</param>
    Task SaveAsync(
        List<PermissionGroupDefinitionRecord> newGroups,
        List<PermissionGroupDefinitionRecord> updatedGroups,
        List<PermissionGroupDefinitionRecord> deletedGroups,
        List<PermissionDefinitionRecord> newPermissions,
        List<PermissionDefinitionRecord> updatedPermissions,
        List<PermissionDefinitionRecord> deletedPermissions,
        CancellationToken cancellationToken = default
    );
}
