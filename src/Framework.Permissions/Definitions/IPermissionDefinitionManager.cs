// Copyright (c) Mahmoud Shaheen, 2024. All rights reserved

namespace Framework.Permissions.Definitions;

public interface IPermissionDefinitionManager
{
    Task<PermissionDefinition> GetAsync(string name);

    Task<PermissionDefinition?> GetOrDefaultAsync(string name);

    Task<IReadOnlyList<PermissionDefinition>> GetPermissionsAsync();

    Task<IReadOnlyList<PermissionGroupDefinition>> GetGroupsAsync();
}
