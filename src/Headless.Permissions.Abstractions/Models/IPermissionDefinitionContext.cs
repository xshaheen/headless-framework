// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Permissions.Models;

public interface IPermissionDefinitionContext
{
    /// <summary>
    /// Gets a pre-defined permission group.
    /// Throws <see cref="InvalidOperationException"/> if can not find the given group.
    /// </summary>
    /// <param name="name">Name of the group</param>
    /// <returns></returns>
    PermissionGroupDefinition GetGroup(string name);

    /// <summary>
    /// Tries to get a pre-defined permission group.
    /// Returns null if can not find the given group.
    /// </summary>
    /// <param name="name">Name of the group</param>
    /// <returns></returns>
    PermissionGroupDefinition? GetGroupOrNull(string name);

    /// <summary>
    /// Tries to add a new permission group.
    /// Throws <see cref="InvalidOperationException"/> if there is a group with the name.
    /// <param name="name">Name of the group</param>
    /// <param name="displayName">Localized display name of the group</param>
    /// </summary>
    PermissionGroupDefinition AddGroup(string name, string? displayName = null);

    /// <summary>
    /// Tries to add a new permission group.
    /// Throws <see cref="InvalidOperationException"/> if there is a group with the name.
    /// </summary>
    PermissionGroupDefinition AddGroup(PermissionGroupDefinition group);

    /// <summary>
    /// Tries to remove a permission group.
    /// Throws <see cref="InvalidOperationException"/> if there is not any group with the name.
    /// </summary>
    /// <param name="name">Name of the group</param>
    void RemoveGroup(string name);

    /// <summary>
    /// Tries to get a pre-defined permission group.
    /// Returns null if you can not find the given group.
    /// <param name="name">Name of the group</param>
    /// </summary>
    PermissionDefinition? GetPermissionOrDefault(string name);
}
