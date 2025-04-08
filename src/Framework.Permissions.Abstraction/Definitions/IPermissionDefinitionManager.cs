// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Framework.Permissions.Models;

namespace Framework.Permissions.Definitions;

public interface IPermissionDefinitionManager
{
    Task<PermissionDefinition?> GetOrDefaultAsync(string name, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<PermissionDefinition>> GetPermissionsAsync(CancellationToken cancellationToken = default);

    Task<IReadOnlyList<PermissionGroupDefinition>> GetGroupsAsync(CancellationToken cancellationToken = default);
}
