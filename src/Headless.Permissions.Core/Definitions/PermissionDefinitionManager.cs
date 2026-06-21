// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;
using Headless.Permissions.Models;

namespace Headless.Permissions.Definitions;

/// <summary>
/// Default implementation of <see cref="IPermissionDefinitionManager"/> that merges the
/// <see cref="IStaticPermissionDefinitionStore"/> (code-defined providers) with the
/// <see cref="IDynamicPermissionDefinitionStore"/> (DB-backed). Static definitions always win over dynamic
/// definitions of the same name so that code-level definitions cannot be silently overridden by DB state.
/// </summary>
public sealed class PermissionDefinitionManager(
    IStaticPermissionDefinitionStore staticStore,
    IDynamicPermissionDefinitionStore dynamicStore
) : IPermissionDefinitionManager
{
    public async Task<PermissionDefinition?> FindAsync(string name, CancellationToken cancellationToken = default)
    {
        Argument.IsNotNull(name);

        return await staticStore.GetOrDefaultPermissionAsync(name, cancellationToken).ConfigureAwait(false)
            ?? await dynamicStore.GetOrDefaultAsync(name, cancellationToken).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<PermissionDefinition>> GetPermissionsAsync(
        CancellationToken cancellationToken = default
    )
    {
        var staticPermissions = await staticStore.GetAllPermissionsAsync(cancellationToken).ConfigureAwait(false);
        var staticPermissionNames = staticPermissions.Select(p => p.Name).ToImmutableHashSet();
        // Prefer static permissions over dynamics
        var dynamicPermissions = await dynamicStore.GetPermissionsAsync(cancellationToken).ConfigureAwait(false);
        var uniqueDynamicPermissions = dynamicPermissions.Where(d => !staticPermissionNames.Contains(d.Name));

        return staticPermissions.Concat(uniqueDynamicPermissions).ToImmutableList();
    }

    public async Task<IReadOnlyList<PermissionGroupDefinition>> GetGroupsAsync(
        CancellationToken cancellationToken = default
    )
    {
        var staticGroups = await staticStore.GetGroupsAsync(cancellationToken).ConfigureAwait(false);
        var staticGroupNames = staticGroups.Select(p => p.Name).ToImmutableHashSet();
        // Prefer static groups over dynamics
        var dynamicGroups = await dynamicStore.GetGroupsAsync(cancellationToken).ConfigureAwait(false);
        var uniqueDynamicGroups = dynamicGroups.Where(d => !staticGroupNames.Contains(d.Name));

        return staticGroups.Concat(uniqueDynamicGroups).ToImmutableList();
    }
}
