// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;
using Headless.Features.Models;

namespace Headless.Features.Definitions;

/// <summary>
/// <see cref="IFeatureDefinitionManager"/> implementation that merges static and dynamic feature definitions,
/// giving precedence to static definitions when both stores contain the same feature or group name.
/// </summary>
public sealed class FeatureDefinitionManager(
    IStaticFeatureDefinitionStore staticStore,
    IDynamicFeatureDefinitionStore dynamicStore
) : IFeatureDefinitionManager
{
    /// <summary>Finds a feature definition by <paramref name="name"/>, checking the static store first, then the dynamic store.</summary>
    /// <param name="name">The unique feature name to search for.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>The matching <see cref="FeatureDefinition"/>, or <see langword="null"/> when absent in both stores.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="name"/> is <see langword="null"/>.</exception>
    public async Task<FeatureDefinition?> FindAsync(string name, CancellationToken cancellationToken = default)
    {
        Argument.IsNotNull(name);

        return await staticStore.GetOrDefaultAsync(name, cancellationToken).ConfigureAwait(false)
            ?? await dynamicStore.GetOrDefaultAsync(name, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Returns all known feature definitions, merging both stores and preferring static definitions over
    /// dynamic ones when their names conflict.
    /// </summary>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>A read-only list of all <see cref="FeatureDefinition"/> instances.</returns>
    public async Task<IReadOnlyList<FeatureDefinition>> GetFeaturesAsync(CancellationToken cancellationToken = default)
    {
        var staticFeatures = await staticStore.GetFeaturesAsync(cancellationToken).ConfigureAwait(false);
        var staticFeatureNames = staticFeatures.Select(p => p.Name).ToImmutableHashSet();

        // Prefer static features over dynamics
        var dynamicFeatures = await dynamicStore.GetFeaturesAsync(cancellationToken).ConfigureAwait(false);
        var uniqueDynamicFeatures = dynamicFeatures.Where(d => !staticFeatureNames.Contains(d.Name));

        return staticFeatures.Concat(uniqueDynamicFeatures).ToImmutableList();
    }

    /// <summary>
    /// Returns all known feature group definitions, merging both stores and preferring static groups over
    /// dynamic ones when their names conflict.
    /// </summary>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>A read-only list of all <see cref="FeatureGroupDefinition"/> instances.</returns>
    public async Task<IReadOnlyList<FeatureGroupDefinition>> GetGroupsAsync(
        CancellationToken cancellationToken = default
    )
    {
        var staticGroups = await staticStore.GetGroupsAsync(cancellationToken).ConfigureAwait(false);
        var staticGroupNames = staticGroups.Select(p => p.Name).ToImmutableHashSet();
        // Prefer static features over dynamics
        var dynamicFeatures = await dynamicStore.GetGroupsAsync(cancellationToken).ConfigureAwait(false);
        var uniqueDynamicFeatures = dynamicFeatures.Where(d => !staticGroupNames.Contains(d.Name));

        return staticGroups.Concat(uniqueDynamicFeatures).ToImmutableList();
    }
}
