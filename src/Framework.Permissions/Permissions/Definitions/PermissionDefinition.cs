using Framework.Arguments;

namespace Framework.Permissions.Permissions.Definitions;

public sealed class PermissionDefinition : ICanAddChildPermission
{
    private string? _displayName;
    private readonly List<PermissionDefinition> _children;

    internal PermissionDefinition(string name, string? displayName = null, bool isEnabled = true)
    {
        Name = Argument.IsNotNull(name);
        DisplayName = displayName ?? name;
        IsEnabled = isEnabled;
        Properties = new(StringComparer.Ordinal);
        Providers = [];
        _children = [];
    }

    /// <summary>Unique name of the permission.</summary>
    public string Name { get; }

    /// <summary>Display name of the permission.</summary>
    public string DisplayName
    {
        get => _displayName!;
        set => _displayName = Argument.IsNotNull(value);
    }

    /// <summary>Parent of this permission if one exists. If set, this permission can be granted only if parent is granted.</summary>
    public PermissionDefinition? Parent { get; private set; }

    /// <summary>
    /// A list of allowed providers to get/set value of this permission.
    /// An empty list indicates that all providers are allowed.
    /// </summary>
    public List<string> Providers { get; }

    /// <summary>Children of this permission.</summary>
    public IReadOnlyList<PermissionDefinition> Children => _children;

    /// <summary>Can be used to get/set custom properties for this permission definition.</summary>
    public Dictionary<string, object?> Properties { get; }

    /// <summary>
    /// <para>
    /// Indicates whether this permission is enabled or disabled.
    /// A permission is normally enabled.
    /// A disabled permission can not be granted to anyone, but it is still
    /// will be available to check its value (while it will always be false).
    /// </para>
    /// <para>
    /// Disabling a permission would be helpful to hide a related application
    /// functionality from users/clients.
    /// </para>
    /// <para>Default: true.</para>
    /// </summary>
    public bool IsEnabled { get; set; }

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

    /// <summary>Adds a child permission to this permission.</summary>
    public PermissionDefinition AddPermission(string name, string? displayName = null, bool isEnabled = true)
    {
        var child = new PermissionDefinition(name, displayName, isEnabled) { Parent = this };

        _children.Add(child);

        return child;
    }

    public override string ToString()
    {
        return $"[{nameof(PermissionDefinition)} {Name}]";
    }
}
