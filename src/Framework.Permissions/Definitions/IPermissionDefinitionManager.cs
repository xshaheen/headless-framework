// Copyright (c) Mahmoud Shaheen, 2024. All rights reserved

using Framework.Kernel.Checks;

namespace Framework.Permissions.Definitions;

public interface IPermissionDefinitionManager
{
    Task<PermissionDefinition?> GetOrDefaultPermissionAsync(string name, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<PermissionDefinition>> GetAllPermissionsAsync(CancellationToken cancellationToken = default);

    Task<IReadOnlyList<PermissionGroupDefinition>> GetAllGroupsAsync(CancellationToken cancellationToken = default);
}

public sealed class PermissionDefinitionManager(
    IStaticPermissionDefinitionStore staticStore,
    IDynamicPermissionDefinitionStore dynamicStore
) : IPermissionDefinitionManager
{
    public async Task<PermissionDefinition?> GetOrDefaultPermissionAsync(
        string name,
        CancellationToken cancellationToken = default
    )
    {
        Argument.IsNotNull(name);

        return await staticStore.GetOrDefaultPermissionAsync(name, cancellationToken)
            ?? await dynamicStore.GetOrNullAsync(name, cancellationToken);
    }

    public async Task<IReadOnlyList<PermissionDefinition>> GetAllPermissionsAsync(
        CancellationToken cancellationToken = default
    )
    {
        var staticPermissions = await staticStore.GetAllPermissionsAsync(cancellationToken);
        var staticPermissionNames = staticPermissions.Select(p => p.Name).ToImmutableHashSet();
        // We prefer static permissions over dynamics
        var dynamicPermissions = await dynamicStore.GetPermissionsAsync(cancellationToken);
        var uniqueDynamicPermissions = dynamicPermissions.Where(d => !staticPermissionNames.Contains(d.Name));

        return staticPermissions.Concat(uniqueDynamicPermissions).ToImmutableList();
    }

    public async Task<IReadOnlyList<PermissionGroupDefinition>> GetAllGroupsAsync(
        CancellationToken cancellationToken = default
    )
    {
        var staticGroups = await staticStore.GetGroupsAsync(cancellationToken);
        var staticGroupNames = staticGroups.Select(p => p.Name).ToImmutableHashSet();
        // We prefer static groups over dynamics
        var dynamicGroups = await dynamicStore.GetGroupsAsync(cancellationToken);
        var uniqueDynamicGroups = dynamicGroups.Where(d => !staticGroupNames.Contains(d.Name));

        return staticGroups.Concat(uniqueDynamicGroups).ToImmutableList();
    }
}
