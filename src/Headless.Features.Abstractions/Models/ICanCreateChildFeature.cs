// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Features.Models;

/// <summary>Implemented by types that can own child <see cref="FeatureDefinition"/> entries (groups and features).</summary>
public interface ICanCreateChildFeature
{
    /// <summary>Creates and registers a child feature definition.</summary>
    /// <param name="name">Unique name of the child feature. Must not be null or white space.</param>
    /// <param name="defaultValue">Default string value for the child feature. <see langword="null"/> means no default.</param>
    /// <param name="displayName">Human-readable display name. Defaults to <paramref name="name"/> when <see langword="null"/>.</param>
    /// <param name="description">Optional description of the child feature's purpose.</param>
    /// <param name="isVisibleToClients">Whether clients can see this feature and its value. Default: <see langword="true"/>.</param>
    /// <param name="isAvailableToHost">Whether the host can use this feature. Default: <see langword="true"/>.</param>
    /// <returns>The newly created child <see cref="FeatureDefinition"/>.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="name"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException"><paramref name="name"/> is empty or white space.</exception>
    FeatureDefinition AddChild(
        string name,
        string? defaultValue = null,
        string? displayName = null,
        string? description = null,
        bool isVisibleToClients = true,
        bool isAvailableToHost = true
    );
}
