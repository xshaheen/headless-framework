// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Features.Models;

/// <summary>
/// Provides read/write access to the feature group registry during the definition phase.
/// Passed to each <see cref="Headless.Features.Definitions.IFeatureDefinitionProvider"/> so that providers can
/// add, look up, or remove feature groups.
/// </summary>
public interface IFeatureDefinitionContext
{
    /// <summary>Returns the feature group with <paramref name="name"/>, or <see langword="null"/> if not found.</summary>
    /// <param name="name">The unique name of the group to look up.</param>
    /// <returns>The matching <see cref="FeatureGroupDefinition"/>, or <see langword="null"/>.</returns>
    FeatureGroupDefinition? GetGroupOrDefault(string name);

    /// <summary>Creates and registers a feature group with the given name.</summary>
    /// <param name="name">Unique name of the group.</param>
    /// <param name="displayName">Human-readable display name. Defaults to <paramref name="name"/> when <see langword="null"/>.</param>
    /// <returns>The newly created <see cref="FeatureGroupDefinition"/>.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="name"/> is <see langword="null"/>.</exception>
    /// <exception cref="InvalidOperationException">A group with the same name is already registered in this context.</exception>
    FeatureGroupDefinition AddGroup(string name, string? displayName = null);

    /// <summary>Removes the feature group with the given name from the context.</summary>
    /// <param name="name">The unique name of the group to remove.</param>
    /// <exception cref="ArgumentNullException"><paramref name="name"/> is <see langword="null"/>.</exception>
    /// <exception cref="InvalidOperationException">No group with the given <paramref name="name"/> exists in this context.</exception>
    void RemoveGroup(string name);
}
