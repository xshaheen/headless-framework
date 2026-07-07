// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Permissions.Models;

/// <summary>
/// The mutable builder passed to <see cref="Definitions.IPermissionDefinitionProvider.Define"/> for declaring
/// permission groups and permissions. Group names must be unique within the context.
/// </summary>
public interface IPermissionDefinitionContext
{
    /// <summary>Gets a previously added permission group by name.</summary>
    /// <param name="name">Name of the group.</param>
    /// <exception cref="InvalidOperationException">Thrown when no group with the given name exists. Use <see cref="GetGroupOrDefault"/> to avoid throwing.</exception>
    PermissionGroupDefinition GetGroup(string name);

    /// <summary>Gets a previously added permission group by name, or <see langword="null"/> if it does not exist.</summary>
    /// <param name="name">Name of the group.</param>
    PermissionGroupDefinition? GetGroupOrDefault(string name);

    /// <summary>Adds a new permission group and returns it so permissions can be chained onto it.</summary>
    /// <param name="name">Unique name of the group.</param>
    /// <param name="displayName">Localized display name of the group; defaults to <paramref name="name"/> when omitted.</param>
    /// <exception cref="InvalidOperationException">Thrown when a group with the same name already exists.</exception>
    PermissionGroupDefinition AddGroup(string name, string? displayName = null);

    /// <summary>Removes a permission group by name.</summary>
    /// <param name="name">Name of the group.</param>
    /// <exception cref="InvalidOperationException">Thrown when no group with the given name exists.</exception>
    void RemoveGroup(string name);

    /// <summary>
    /// Finds a permission by name anywhere in the context, searching every group and its nested child
    /// permissions recursively.
    /// </summary>
    /// <param name="name">Name of the permission.</param>
    /// <returns>The matching permission, or <see langword="null"/> if none is defined.</returns>
    PermissionDefinition? GetPermissionOrDefault(string name);
}
