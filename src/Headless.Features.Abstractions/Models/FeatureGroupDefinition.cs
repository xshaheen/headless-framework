// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;
using Headless.Primitives;

namespace Headless.Features.Models;

/// <summary>Describes a logical group that organizes one or more related <see cref="FeatureDefinition"/> instances.</summary>
[PublicAPI]
public sealed class FeatureGroupDefinition : ICanAddChildFeature, IHasExtraProperties
{
    private readonly List<FeatureDefinition> _features;

    /// <summary>Creates a new <see cref="FeatureGroupDefinition"/> with the specified name.</summary>
    /// <param name="name">Unique name of the group.</param>
    /// <param name="displayName">Human-readable display name. Defaults to <paramref name="name"/> when <see langword="null"/>.</param>
    internal FeatureGroupDefinition(string name, string? displayName = null)
    {
        Name = name;
        DisplayName = displayName ?? Name;
        _features = [];
    }

    /// <summary>Unique name of the group.</summary>
    public string Name { get; }

    /// <summary>Display name of the group.</summary>
    [field: AllowNull, MaybeNull]
    public string DisplayName
    {
        get;
        set => field = Argument.IsNotNull(value);
    }

    /// <summary>List of features in this group.</summary>
    public IReadOnlyList<FeatureDefinition> Features => _features;

    /// <summary>Bag of custom properties for this group.</summary>
    public ExtraProperties ExtraProperties { get; } = [];

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

    /// <summary>Adds a top-level feature to this group.</summary>
    /// <param name="options">The feature name and optional metadata.</param>
    /// <returns>The newly created <see cref="FeatureDefinition"/> added to this group.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="options"/> is <see langword="null"/>.</exception>
    public FeatureDefinition AddChild(FeatureDefinitionCreateOptions options)
    {
        Argument.IsNotNull(options);

        var feature = new FeatureDefinition(
            options.Name,
            options.DefaultValue,
            options.DisplayName,
            options.Description,
            options.IsVisibleToClients,
            options.IsAvailableToHost
        );

        _features.Add(feature);

        return feature;
    }

    /// <summary>
    /// Returns a flat list of every <see cref="FeatureDefinition"/> in this group, including all nested children,
    /// in depth-first order.
    /// </summary>
    /// <returns>A flat list containing every feature and its descendants.</returns>
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

    /// <inheritdoc/>
    public override string ToString()
    {
        return $"[{nameof(FeatureGroupDefinition)} {Name}]";
    }
}
