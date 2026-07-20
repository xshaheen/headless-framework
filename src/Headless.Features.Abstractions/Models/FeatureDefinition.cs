// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;
using Headless.Primitives;

namespace Headless.Features.Models;

/// <summary>Describes a feature, its metadata, and its position in the feature hierarchy.</summary>
[PublicAPI]
public sealed class FeatureDefinition : ICanAddChildFeature, IHasExtraProperties
{
    private readonly List<FeatureDefinition> _children;

    /// <summary>Creates a new <see cref="FeatureDefinition"/> with the specified metadata.</summary>
    /// <param name="name">Unique name of the feature. Must not be null or white space.</param>
    /// <param name="defaultValue">Default string value for the feature. <see langword="null"/> means no default.</param>
    /// <param name="displayName">Human-readable display name. Defaults to <paramref name="name"/> when <see langword="null"/>.</param>
    /// <param name="description">Optional description of the feature's purpose.</param>
    /// <param name="isVisibleToClients">Whether clients can see this feature and its value. Default: <see langword="true"/>.</param>
    /// <param name="isAvailableToHost">Whether the host can use this feature. Default: <see langword="true"/>.</param>
    /// <exception cref="ArgumentNullException"><paramref name="name"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException"><paramref name="name"/> is empty or white space.</exception>
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

    /// <summary>Whether clients can see this feature and its value. Default: <see langword="true"/>.</summary>
    public bool IsVisibleToClients { get; set; }

    /// <summary>Whether the host application can use this feature. Default: <see langword="true"/>.</summary>
    public bool IsAvailableToHost { get; set; }

    /// <summary>
    /// A list of allowed providers to get/set value of this feature.
    /// An empty list indicates that all providers are allowed.
    /// </summary>
    public List<string> Providers { get; }

    /// <summary>Bag of custom properties for this feature.</summary>
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

    /// <summary>Creates and registers a child feature nested under this feature.</summary>
    /// <param name="options">The child feature name and optional metadata.</param>
    /// <returns>The newly created child <see cref="FeatureDefinition"/>. Its <see cref="Parent"/> is set to this instance automatically.</returns>
    /// <remarks>A child feature can only be enabled when its parent feature is also enabled.</remarks>
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
        )
        {
            Parent = this,
        };

        _children.Add(feature);

        return feature;
    }

    /// <summary>Removes a child feature by name.</summary>
    /// <param name="name">The name of the child feature to remove.</param>
    /// <exception cref="InvalidOperationException">No child with the given <paramref name="name"/> exists under this feature.</exception>
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

    /// <inheritdoc/>
    public override string ToString()
    {
        return $"[{nameof(FeatureDefinition)}: {Name}]";
    }
}
