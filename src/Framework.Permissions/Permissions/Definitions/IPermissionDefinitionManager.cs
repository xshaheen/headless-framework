namespace Framework.Permissions.Permissions.Definitions;

public interface IPermissionDefinitionManager
{
    Task<PermissionDefinition> GetAsync(string name);

    Task<PermissionDefinition?> GetOrNullAsync(string name);

    Task<IReadOnlyList<PermissionDefinition>> GetPermissionsAsync();

    Task<IReadOnlyList<PermissionGroupDefinition>> GetGroupsAsync();
}
