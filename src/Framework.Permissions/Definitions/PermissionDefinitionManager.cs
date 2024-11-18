// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Framework.Checks;
using Framework.Permissions.Models;

namespace Framework.Permissions.Definitions;

public interface IPermissionDefinitionManager
{
    Task<PermissionDefinition?> GetOrDefaultAsync(string name, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<PermissionDefinition>> GetPermissionsAsync(CancellationToken cancellationToken = default);

    Task<IReadOnlyList<PermissionGroupDefinition>> GetGroupsAsync(CancellationToken cancellationToken = default);
}

public sealed class PermissionDefinitionManager(
    IStaticPermissionDefinitionStore staticStore,
    IDynamicPermissionDefinitionStore dynamicStore
) : IPermissionDefinitionManager
{
    public async Task<PermissionDefinition?> GetOrDefaultAsync(
        string name,
        CancellationToken cancellationToken = default
    )
    {
        Argument.IsNotNull(name);

        return await staticStore.GetOrDefaultPermissionAsync(name, cancellationToken)
            ?? await dynamicStore.GetOrDefaultAsync(name, cancellationToken);
    }

    public async Task<IReadOnlyList<PermissionDefinition>> GetPermissionsAsync(
        CancellationToken cancellationToken = default
    )
    {
        var staticPermissions = await staticStore.GetAllPermissionsAsync(cancellationToken);
        var staticPermissionNames = staticPermissions.Select(p => p.Name).ToImmutableHashSet();
        // Prefer static permissions over dynamics
        var dynamicPermissions = await dynamicStore.GetPermissionsAsync(cancellationToken);
        var uniqueDynamicPermissions = dynamicPermissions.Where(d => !staticPermissionNames.Contains(d.Name));

        return staticPermissions.Concat(uniqueDynamicPermissions).ToImmutableList();
    }

    public async Task<IReadOnlyList<PermissionGroupDefinition>> GetGroupsAsync(
        CancellationToken cancellationToken = default
    )
    {
        var staticGroups = await staticStore.GetGroupsAsync(cancellationToken);
        var staticGroupNames = staticGroups.Select(p => p.Name).ToImmutableHashSet();
        // Prefer static groups over dynamics
        var dynamicGroups = await dynamicStore.GetGroupsAsync(cancellationToken);
        var uniqueDynamicGroups = dynamicGroups.Where(d => !staticGroupNames.Contains(d.Name));

        return staticGroups.Concat(uniqueDynamicGroups).ToImmutableList();
    }
}
