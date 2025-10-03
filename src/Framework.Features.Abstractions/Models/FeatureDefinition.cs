// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Diagnostics.CodeAnalysis;
using Framework.Checks;

namespace Framework.Features.Models;

public sealed class FeatureDefinition : ICanCreateChildFeature
{
    private readonly List<FeatureDefinition> _children;

    internal FeatureDefinition(
        string name,
        string? defaultValue = null,
        string? displayName = null,
        string? description = null,
        bool isVisibleToClients = true,
        bool isAvailableToHost = true
    )
    {
        Name = Argument.IsNotNullOrWhiteSpace(name);
        DefaultValue = defaultValue;
        DisplayName = displayName ?? name;
        Description = description;
        IsVisibleToClients = isVisibleToClients;
        IsAvailableToHost = isAvailableToHost;
        Properties = new(StringComparer.Ordinal);
        Providers = [];
        _children = [];
    }

    /// <summary>
    /// Unique name of the feature.
    /// </summary>
    public string Name { get; }

    /// <summary>Display name of the feature.</summary>
    [field: AllowNull, MaybeNull]
    public string DisplayName
    {
        get;
        set => field = Argument.IsNotNull(value);
    }

    /// <summary>Display description of the feature.</summary>
    public string? Description { get; set; }

    /// <summary>Parent of this feature, if one exists. If set, this feature can be enabled only if the parent is enabled.</summary>
    public FeatureDefinition? Parent { get; private set; }

    /// <summary>List of child features.</summary>
    public IReadOnlyList<FeatureDefinition> Children => _children;

    /// <summary>Default value of the feature.</summary>
    public string? DefaultValue { get; set; }

    /// <summary>Can clients see this feature and it's value. Default: true.</summary>
    public bool IsVisibleToClients { get; set; }

    /// <summary>Can host use this feature. Default: true.</summary>
    public bool IsAvailableToHost { get; set; }

    /// <summary>
    /// A list of allowed providers to get/set value of this feature.
    /// An empty list indicates that all providers are allowed.
    /// </summary>
    public List<string> Providers { get; }

    /// <summary>Can be used to get/set custom properties for this feature.</summary>
    public Dictionary<string, object?> Properties { get; }

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

    /// <summary>Adds a child feature.</summary>
    /// <returns>Returns a newly created child feature</returns>
    public FeatureDefinition AddChild(
        string name,
        string? defaultValue = null,
        string? displayName = null,
        string? description = null,
        bool isVisibleToClients = true,
        bool isAvailableToHost = true
    )
    {
        var feature = new FeatureDefinition(
            name,
            defaultValue,
            displayName,
            description,
            isVisibleToClients,
            isAvailableToHost
        )
        {
            Parent = this,
        };

        _children.Add(feature);

        return feature;
    }

    public void RemoveChild(string name)
    {
        var featureToRemove =
            _children.Find(f => string.Equals(f.Name, name, StringComparison.Ordinal))
            ?? throw new InvalidOperationException(
                $"Could not find a feature named '{name}' in the Children of this feature '{Name}'."
            );

        featureToRemove.Parent = null;
        _children.Remove(featureToRemove);
    }

    public override string ToString() => $"[{nameof(FeatureDefinition)}: {Name}]";
}
