using Framework.Arguments;

namespace Framework.Permissions.Permissions.Definitions;

public sealed class PermissionGroupDefinition : ICanAddChildPermission
{
    private string? _displayName;
    private readonly List<PermissionDefinition> _permissions;

    internal PermissionGroupDefinition(string name, string? displayName = null)
    {
        Name = name;
        DisplayName = displayName ?? name;
        Properties = new(StringComparer.Ordinal);
        _permissions = [];
    }

    /// <summary>Unique name of the group.</summary>
    public string Name { get; }

    /// <summary>The display name of the group.</summary>
    public string DisplayName
    {
        get => _displayName!;
        set => _displayName = Argument.IsNotNull(value);
    }

    /// <summary>A list of custom properties for this permission group.</summary>
    public Dictionary<string, object?> Properties { get; }

    /// <summary>List of permissions in this group.</summary>
    public IReadOnlyList<PermissionDefinition> Permissions => _permissions;

    /// <summary>Gets/sets a key-value on the <see cref="Properties"/>.</summary>
    /// <param name="name">Name of the property</param>
    /// <returns>
    /// Returns the value in the <see cref="Properties"/> dictionary by given <paramref name="name"/>.
    /// Returns null if given <paramref name="name"/> is not present in the <see cref="Properties"/> dictionary.
    /// </returns>
    public object? this[string name]
    {
        get => Properties.GetOrDefault(name);
        set => Properties[name] = value;
    }

    public List<PermissionDefinition> GetFlatPermissions()
    {
        var permissions = new List<PermissionDefinition>();

        foreach (var permission in _permissions)
        {
            _AddPermissionToListRecursively(permissions, permission);
        }

        return permissions;
    }

    public PermissionDefinition? GetPermissionOrDefault(string name)
    {
        Argument.IsNotNull(name);

        return _GetPermissionOrDefaultRecursively(Permissions, name);
    }

    public PermissionDefinition AddPermission(string name, string? displayName = null, bool isEnabled = true)
    {
        var permission = new PermissionDefinition(name, displayName, isEnabled);
        _permissions.Add(permission);

        return permission;
    }

    private static PermissionDefinition? _GetPermissionOrDefaultRecursively(
        IReadOnlyList<PermissionDefinition> permissions,
        string name
    )
    {
        foreach (var permission in permissions)
        {
            if (string.Equals(permission.Name, name, StringComparison.Ordinal))
            {
                return permission;
            }

            var childPermission = _GetPermissionOrDefaultRecursively(permission.Children, name);

            if (childPermission is not null)
            {
                return childPermission;
            }
        }

        return null;
    }

    private static void _AddPermissionToListRecursively(
        List<PermissionDefinition> list,
        PermissionDefinition permission
    )
    {
        list.Add(permission);

        foreach (var child in permission.Children)
        {
            _AddPermissionToListRecursively(list, child);
        }
    }

    public override string ToString() => $"[{nameof(PermissionGroupDefinition)} {Name}]";
}
