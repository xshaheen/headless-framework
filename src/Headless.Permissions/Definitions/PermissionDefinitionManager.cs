// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;

namespace Headless.Permissions;

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
    // Volatile: the manager is a singleton and the snapshots are published without a lock, matching
    // FeatureDefinitionManager's identical pattern. Racing recomputes are benign (same inputs produce an
    // equivalent merge); unordered publication of a fresh object is not.
    private volatile MergedSnapshot<PermissionDefinition>? _permissionsSnapshot;
    private volatile MergedSnapshot<PermissionGroupDefinition>? _groupsSnapshot;

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
        var dynamicPermissions = await dynamicStore.GetPermissionsAsync(cancellationToken).ConfigureAwait(false);

        var snapshot = _permissionsSnapshot;

        if (snapshot?.Matches(staticPermissions, dynamicPermissions) == true)
        {
            return snapshot.Merged;
        }

        var staticPermissionNames = staticPermissions.Select(p => p.Name).ToImmutableHashSet(StringComparer.Ordinal);
        // Prefer static permissions over dynamics
        var uniqueDynamicPermissions = dynamicPermissions.Where(d => !staticPermissionNames.Contains(d.Name));
        var merged = staticPermissions.Concat(uniqueDynamicPermissions).ToImmutableList();

        _permissionsSnapshot = new MergedSnapshot<PermissionDefinition>(staticPermissions, dynamicPermissions, merged);

        return merged;
    }

    public async Task<IReadOnlyList<PermissionGroupDefinition>> GetGroupsAsync(
        CancellationToken cancellationToken = default
    )
    {
        var staticGroups = await staticStore.GetGroupsAsync(cancellationToken).ConfigureAwait(false);
        var dynamicGroups = await dynamicStore.GetGroupsAsync(cancellationToken).ConfigureAwait(false);

        var snapshot = _groupsSnapshot;

        if (snapshot?.Matches(staticGroups, dynamicGroups) == true)
        {
            return snapshot.Merged;
        }

        var staticGroupNames = staticGroups.Select(p => p.Name).ToImmutableHashSet(StringComparer.Ordinal);
        // Prefer static groups over dynamics
        var uniqueDynamicGroups = dynamicGroups.Where(d => !staticGroupNames.Contains(d.Name));
        var mergedGroups = staticGroups.Concat(uniqueDynamicGroups).ToImmutableList();

        _groupsSnapshot = new MergedSnapshot<PermissionGroupDefinition>(staticGroups, dynamicGroups, mergedGroups);

        return mergedGroups;
    }

    /// <summary>
    /// A merged view together with the two source references it was built from (FeatureDefinitionManager's
    /// shape). Both stores hand out immutable snapshots that are swapped wholesale on refresh, so reference
    /// equality on the sources proves the merge is current; without it the whole catalog was re-hashed and
    /// re-concatenated on every batch permission check.
    /// </summary>
    private sealed class MergedSnapshot<T>(
        IReadOnlyCollection<T> staticDefinitions,
        IReadOnlyCollection<T> dynamicDefinitions,
        IReadOnlyList<T> merged
    )
    {
        public IReadOnlyList<T> Merged => merged;

        public bool Matches(IReadOnlyCollection<T> currentStatic, IReadOnlyCollection<T> currentDynamic)
        {
            return ReferenceEquals(staticDefinitions, currentStatic)
                && ReferenceEquals(dynamicDefinitions, currentDynamic);
        }
    }
}
