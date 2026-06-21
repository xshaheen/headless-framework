// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Features.Models;

namespace Headless.Features.Definitions;

/// <summary>Manages the set of all registered feature definitions.</summary>
/// <remarks>
/// The default implementation merges two stores: a <em>static</em> store populated at startup from
/// all registered <see cref="IFeatureDefinitionProvider"/> implementations, and an optional
/// <em>dynamic</em> store backed by the database. When both stores contain a definition for the same
/// feature or group name, the static definition takes precedence.
/// </remarks>
public interface IFeatureDefinitionManager
{
    /// <summary>Finds a feature definition by name, or returns <see langword="null"/> if not found.</summary>
    /// <param name="name">The unique feature name to look up.</param>
    /// <param name="cancellationToken">The abort token.</param>
    /// <returns>The matching <see cref="FeatureDefinition"/>, or <see langword="null"/> if no feature with that name is registered.</returns>
    Task<FeatureDefinition?> FindAsync(string name, CancellationToken cancellationToken = default);

    /// <summary>Returns all registered feature definitions.</summary>
    /// <param name="cancellationToken">The abort token.</param>
    /// <returns>A read-only list of every <see cref="FeatureDefinition"/> registered across all providers.</returns>
    Task<IReadOnlyList<FeatureDefinition>> GetFeaturesAsync(CancellationToken cancellationToken = default);

    /// <summary>Returns all registered feature group definitions.</summary>
    /// <param name="cancellationToken">The abort token.</param>
    /// <returns>A read-only list of every <see cref="FeatureGroupDefinition"/> registered across all providers.</returns>
    Task<IReadOnlyList<FeatureGroupDefinition>> GetGroupsAsync(CancellationToken cancellationToken = default);
}
