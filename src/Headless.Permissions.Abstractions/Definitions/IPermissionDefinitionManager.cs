// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Permissions.Models;

namespace Headless.Permissions.Definitions;

/// <summary>
/// Read-only access to the merged set of permission definitions contributed by the registered
/// <see cref="IPermissionDefinitionProvider"/> instances (the static store) and, when enabled, the
/// dynamic store. When a name exists in both stores, the static definition wins.
/// </summary>
public interface IPermissionDefinitionManager
{
    /// <summary>Finds a single permission definition by its unique name across the static and dynamic stores.</summary>
    /// <returns>The definition, or <see langword="null"/> if no permission with that name is defined.</returns>
    Task<PermissionDefinition?> FindAsync(string name, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets every defined permission flattened across all groups and their child hierarchies (including
    /// disabled permissions). The result is de-duplicated by name with static definitions taking precedence
    /// over dynamic ones.
    /// </summary>
    Task<IReadOnlyList<PermissionDefinition>> GetPermissionsAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets all defined permission groups, with static groups taking precedence over dynamic groups of the
    /// same name.
    /// </summary>
    Task<IReadOnlyList<PermissionGroupDefinition>> GetGroupsAsync(CancellationToken cancellationToken = default);
}
