// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Framework.Checks;

namespace Framework.Permissions.Models;

public sealed class PermissionDefinitionContext : IPermissionDefinitionContext
{
    public Dictionary<string, PermissionGroupDefinition> Groups { get; } = new(StringComparer.Ordinal);

    public PermissionGroupDefinition AddGroup(string name, string? displayName = null)
    {
        Argument.IsNotNull(name);

        var group = new PermissionGroupDefinition(name, displayName);

        return AddGroup(group);
    }

    public PermissionGroupDefinition AddGroup(PermissionGroupDefinition group)
    {
        Argument.IsNotNull(group);

        if (Groups.ContainsKey(group.Name))
        {
            throw new InvalidOperationException(
                $"There is already an existing permission group with name: {group.Name}"
            );
        }

        return Groups[group.Name] = group;
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

    public PermissionDefinition? GetPermissionOrDefault(string name)
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

[PublicAPI]
public static class PermissionDefinitionContextExtensions
{
    /// <summary>
    /// Finds and disables a permission with the given <paramref name="name"/>.
    /// Returns false if given permission was not found.
    /// </summary>
    /// <param name="context">Permission definition context</param>
    /// <param name="name">Name of the permission</param>
    /// <returns>
    /// Returns true if given permission was found.
    /// Returns false if given permission was not found.
    /// </returns>
    public static bool TryDisablePermission(this IPermissionDefinitionContext context, string name)
    {
        Argument.IsNotNull(context);
        Argument.IsNotNull(name);

        var permission = context.GetPermissionOrDefault(name);

        if (permission is null)
        {
            return false;
        }

        permission.IsEnabled = false;

        return true;
    }
}
