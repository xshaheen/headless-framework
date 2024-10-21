// Copyright (c) Mahmoud Shaheen, 2024. All rights reserved

using Framework.Features.Definitions;
using Framework.Kernel.Checks;

namespace Framework.Features.Models;

public sealed class FeatureGroupDefinition : ICanCreateChildFeature
{
    private string? _displayName;
    private readonly List<FeatureDefinition> _features;

    internal FeatureGroupDefinition(string name, string? displayName = null)
    {
        Name = name;
        DisplayName = displayName ?? Name;
        Properties = new(StringComparer.Ordinal);
        _features = [];
    }

    /// <summary>Unique name of the group.</summary>
    public string Name { get; }

    /// <summary>Display name of the group.</summary>
    public string DisplayName
    {
        get => _displayName!;
        set => _displayName = Argument.IsNotNull(value);
    }

    /// <summary>List of features in this group.</summary>
    public IReadOnlyList<FeatureDefinition> Features => _features;

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
        );

        _features.Add(feature);

        return feature;
    }

    public List<FeatureDefinition> GetFlatFeatures()
    {
        var list = new List<FeatureDefinition>();

        foreach (var feature in _features)
        {
            addFeatureToListRecursively(list, feature);
        }

        return list;

        static void addFeatureToListRecursively(List<FeatureDefinition> list, FeatureDefinition feature)
        {
            list.Add(feature);

            foreach (var child in feature.Children)
            {
                addFeatureToListRecursively(list, child);
            }
        }
    }

    public override string ToString() => $"[{nameof(FeatureGroupDefinition)} {Name}]";
}
