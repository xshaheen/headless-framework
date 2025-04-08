// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Framework.Permissions.Entities;

namespace Framework.Permissions.Repositories;

public interface IPermissionDefinitionRecordRepository
{
    Task<List<PermissionDefinitionRecord>> GetPermissionsListAsync(CancellationToken cancellationToken = default);

    Task<List<PermissionGroupDefinitionRecord>> GetGroupsListAsync(CancellationToken cancellationToken = default);

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
