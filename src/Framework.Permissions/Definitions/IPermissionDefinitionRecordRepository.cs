using Framework.Permissions.Entities;

namespace Framework.Permissions.Definitions;

public interface IPermissionDefinitionRecordRepository
{
    Task<PermissionDefinitionRecord> FindPermissionByNameAsync(
        string name,
        CancellationToken cancellationToken = default
    );

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
