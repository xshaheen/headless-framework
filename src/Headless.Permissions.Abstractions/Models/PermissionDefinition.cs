// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;
using Headless.Primitives;

namespace Headless.Permissions.Models;

/// <summary>
/// A single permission within a <see cref="PermissionGroupDefinition"/>. Permissions form a tree: a child is
/// only effectively grantable when its <see cref="Parent"/> chain is also granted. Instances are created through
/// <see cref="AddChild"/> or <see cref="IPermissionDefinitionContext"/> rather than constructed directly.
/// </summary>
[PublicAPI]
public sealed class PermissionDefinition : ICanAddChildPermission, IHasExtraProperties
{
    private readonly List<PermissionDefinition> _children;

    internal PermissionDefinition(string name, string? displayName = null, bool isEnabled = true)
    {
        Name = Argument.IsNotNull(name);
        DisplayName = displayName ?? name;
        IsEnabled = isEnabled;
        Providers = [];
        _children = [];
    }

    /// <summary>Unique name of the permission.</summary>
    public string Name { get; }

    /// <summary>Display name of the permission.</summary>
    [field: AllowNull, MaybeNull]
    public string DisplayName
    {
        get;
        set => field = Argument.IsNotNull(value);
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

    /// <summary>Bag of custom properties for this permission definition.</summary>
    public ExtraProperties ExtraProperties { get; } = [];

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

    /// <summary>Gets/sets a key-value on the <see cref="ExtraProperties"/>.</summary>
    /// <param name="name">Name of the property</param>
    /// <returns>
    /// Returns the value in the <see cref="ExtraProperties"/> dictionary by given <paramref name="name"/>.
    /// Returns null if given <paramref name="name"/> is not present in the <see cref="ExtraProperties"/> dictionary.
    /// </returns>
    public object? this[string name]
    {
        get => ExtraProperties.GetOrDefault(name);
        set => ExtraProperties[name] = value;
    }

    /// <summary>Adds a child permission whose <see cref="Parent"/> is set to this permission, and returns it for further chaining.</summary>
    public PermissionDefinition AddChild(string name, string? displayName = null, bool isEnabled = true)
    {
        var child = new PermissionDefinition(name, displayName, isEnabled) { Parent = this };

        _children.Add(child);

        return child;
    }

    /// <summary>Removes a child permission by name.</summary>
    /// <param name="name">The name of the child permission to remove.</param>
    /// <exception cref="InvalidOperationException">No child with the given <paramref name="name"/> exists under this permission.</exception>
    public void RemoveChild(string name)
    {
        var childToRemove =
            _children.Find(c => string.Equals(c.Name, name, StringComparison.Ordinal))
            ?? throw new InvalidOperationException(
                $"Could not find a permission named '{name}' in the Children of this permission '{Name}'."
            );

        childToRemove.Parent = null;
        _children.Remove(childToRemove);
    }

    public override string ToString()
    {
        return $"[{nameof(PermissionDefinition)} {Name}]";
    }
}
