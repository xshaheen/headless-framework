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
    private MergedSnapshot<FeatureDefinition>? _featuresSnapshot;
    private MergedSnapshot<FeatureGroupDefinition>? _groupsSnapshot;

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
        var dynamicFeatures = await dynamicStore.GetFeaturesAsync(cancellationToken).ConfigureAwait(false);

        var snapshot = _featuresSnapshot;

        if (snapshot is not null && snapshot.Matches(staticFeatures, dynamicFeatures))
        {
            return snapshot.Merged;
        }

        var staticFeatureNames = staticFeatures.Select(p => p.Name).ToImmutableHashSet();

        // Prefer static features over dynamics
        var uniqueDynamicFeatures = dynamicFeatures.Where(d => !staticFeatureNames.Contains(d.Name));
        var merged = staticFeatures.Concat(uniqueDynamicFeatures).ToImmutableList();

        _featuresSnapshot = new MergedSnapshot<FeatureDefinition>(staticFeatures, dynamicFeatures, merged);

        return merged;
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
        var dynamicGroups = await dynamicStore.GetGroupsAsync(cancellationToken).ConfigureAwait(false);

        var snapshot = _groupsSnapshot;

        if (snapshot is not null && snapshot.Matches(staticGroups, dynamicGroups))
        {
            return snapshot.Merged;
        }

        var staticGroupNames = staticGroups.Select(p => p.Name).ToImmutableHashSet();

        // Prefer static features over dynamics
        var uniqueDynamicGroups = dynamicGroups.Where(d => !staticGroupNames.Contains(d.Name));
        var merged = staticGroups.Concat(uniqueDynamicGroups).ToImmutableList();

        _groupsSnapshot = new MergedSnapshot<FeatureGroupDefinition>(staticGroups, dynamicGroups, merged);

        return merged;
    }

    /// <summary>
    /// A merged view together with the two source references it was built from. Both stores hand out
    /// immutable snapshots that are swapped wholesale on refresh, never mutated in place, so reference
    /// equality on the sources is a sound signal that the merge is still current. Without it the whole
    /// catalog was re-hashed and re-concatenated on every feature check.
    /// </summary>
    private sealed class MergedSnapshot<T>(
        IReadOnlyList<T> staticDefinitions,
        IReadOnlyList<T> dynamicDefinitions,
        IReadOnlyList<T> merged
    )
    {
        public IReadOnlyList<T> Merged => merged;

        public bool Matches(IReadOnlyList<T> currentStatic, IReadOnlyList<T> currentDynamic)
        {
            return ReferenceEquals(staticDefinitions, currentStatic)
                && ReferenceEquals(dynamicDefinitions, currentDynamic);
        }
    }
}
