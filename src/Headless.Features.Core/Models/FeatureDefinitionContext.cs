// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;

namespace Headless.Features.Models;

/// <summary>
/// Default <see cref="IFeatureDefinitionContext"/> implementation used by <see cref="Headless.Features.Definitions.IFeatureDefinitionProvider"/> instances
/// to register feature groups and their features during application startup.
/// </summary>
public sealed class FeatureDefinitionContext : IFeatureDefinitionContext
{
    /// <summary>All registered feature groups, keyed by name using ordinal comparison.</summary>
    internal Dictionary<string, FeatureGroupDefinition> Groups { get; } = new(StringComparer.Ordinal);

    /// <summary>Adds a new feature group with the given <paramref name="name"/> and optional <paramref name="displayName"/>.</summary>
    /// <param name="name">The unique name for the feature group.</param>
    /// <param name="displayName">An optional human-readable display name.</param>
    /// <returns>The newly created <see cref="FeatureGroupDefinition"/>.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="name"/> is <see langword="null"/>.</exception>
    /// <exception cref="InvalidOperationException">A feature group with the same <paramref name="name"/> already exists.</exception>
    public FeatureGroupDefinition AddGroup(string name, string? displayName = null)
    {
        Argument.IsNotNull(name);

        return AddGroup(new FeatureGroupDefinition(name, displayName));
    }

    /// <summary>Registers the provided <paramref name="definition"/> as a feature group.</summary>
    /// <param name="definition">The feature group definition to register.</param>
    /// <returns>The registered <see cref="FeatureGroupDefinition"/>.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="definition"/> is <see langword="null"/>.</exception>
    /// <exception cref="InvalidOperationException">A feature group with the same name already exists.</exception>
    public FeatureGroupDefinition AddGroup(FeatureGroupDefinition definition)
    {
        Argument.IsNotNull(definition);

        if (Groups.ContainsKey(definition.Name))
        {
            throw new InvalidOperationException(
                $"There is already an existing feature group with name: {definition.Name}"
            );
        }

        return Groups[definition.Name] = definition;
    }

    /// <summary>Returns the feature group with the given <paramref name="name"/>, or <see langword="null"/> if not found.</summary>
    /// <param name="name">The unique name of the feature group to look up.</param>
    /// <returns>The matching <see cref="FeatureGroupDefinition"/>, or <see langword="null"/> when absent.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="name"/> is <see langword="null"/>.</exception>
    public FeatureGroupDefinition? GetGroupOrDefault(string name)
    {
        Argument.IsNotNull(name);

        return !Groups.TryGetValue(name, out var value) ? null : value;
    }

    /// <summary>Removes the feature group with the given <paramref name="name"/> from this context.</summary>
    /// <param name="name">The unique name of the feature group to remove.</param>
    /// <exception cref="ArgumentNullException"><paramref name="name"/> is <see langword="null"/>.</exception>
    /// <exception cref="InvalidOperationException">No feature group with the given <paramref name="name"/> exists in this context.</exception>
    public void RemoveGroup(string name)
    {
        Argument.IsNotNull(name);

        if (!Groups.ContainsKey(name))
        {
            throw new InvalidOperationException($"Undefined feature group: '{name}'.");
        }

        Groups.Remove(name);
    }
}
