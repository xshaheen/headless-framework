// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;

namespace Headless.Features.Models;

/// <summary>Specifies the metadata used to create a feature definition.</summary>
[PublicAPI]
public sealed class FeatureDefinitionCreateOptions
{
    /// <summary>Creates options for a feature with the specified unique name.</summary>
    /// <param name="name">The unique feature name.</param>
    /// <exception cref="ArgumentNullException"><paramref name="name"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException"><paramref name="name"/> is empty or consists only of white-space characters.</exception>
    public FeatureDefinitionCreateOptions(string name)
    {
        Name = Argument.IsNotNullOrWhiteSpace(name);
    }

    /// <summary>Gets the unique feature name.</summary>
    public string Name { get; }

    /// <summary>Gets the optional default value.</summary>
    public string? DefaultValue { get; init; }

    /// <summary>Gets the optional display name. When omitted, <see cref="Name"/> is used.</summary>
    public string? DisplayName { get; init; }

    /// <summary>Gets the optional feature description.</summary>
    public string? Description { get; init; }

    /// <summary>Gets whether clients may see the feature and its value. The default is <see langword="true"/>.</summary>
    public bool IsVisibleToClients { get; init; } = true;

    /// <summary>Gets whether the host may use the feature. The default is <see langword="true"/>.</summary>
    public bool IsAvailableToHost { get; init; } = true;
}
