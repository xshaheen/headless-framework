// Copyright (c) Mahmoud Shaheen, 2024. All rights reserved

using Framework.Kernel.Checks;

namespace Framework.Permissions.Permissions.Definitions;

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
    PermissionDefinition? GetPermissionOrNull(string name);
}

public sealed class PermissionDefinitionContext(IServiceProvider serviceProvider) : IPermissionDefinitionContext
{
    public IServiceProvider ServiceProvider { get; } = serviceProvider;

    public Dictionary<string, PermissionGroupDefinition> Groups { get; } = new(StringComparer.Ordinal);

    public PermissionGroupDefinition AddGroup(string name, string? displayName = null)
    {
        Argument.IsNotNull(name);

        if (Groups.ContainsKey(name))
        {
            throw new InvalidOperationException($"There is already an existing permission group with name: {name}");
        }

        return Groups[name] = new PermissionGroupDefinition(name, displayName);
    }

    public PermissionGroupDefinition GetGroup(string name)
    {
        var group = GetGroupOrNull(name);

        return group
            ?? throw new InvalidOperationException(
                $"Could not find a permission definition group with the given name: {name}"
            );
    }

    public PermissionGroupDefinition? GetGroupOrNull(string name)
    {
        Argument.IsNotNull(name);

        return Groups.TryGetValue(name, out var value) ? value : null;
    }

    public void RemoveGroup(string name)
    {
        Argument.IsNotNull(name);

        if (!Groups.ContainsKey(name))
        {
            throw new InvalidOperationException($"Not found permission group with name: {name}");
        }

        Groups.Remove(name);
    }

    public PermissionDefinition? GetPermissionOrNull(string name)
    {
        Argument.IsNotNull(name);

        foreach (var groupDefinition in Groups.Values)
        {
            var permissionDefinition = groupDefinition.GetPermissionOrDefault(name);

            if (permissionDefinition is not null)
            {
                return permissionDefinition;
            }
        }

        return null;
    }
}
