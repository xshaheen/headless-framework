// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;

namespace Headless.Settings;

/// <summary>
/// Default implementation of <see cref="ISettingDefinitionManager"/> that resolves setting definitions
/// from the static store first, falling back to the dynamic store. When listing all definitions,
/// static entries take precedence over dynamic ones with the same name.
/// </summary>
public sealed class SettingDefinitionManager(
    IStaticSettingDefinitionStore staticStore,
    IDynamicSettingDefinitionStore dynamicStore
) : ISettingDefinitionManager
{
    // Volatile: the manager is a singleton and the snapshot publishes without a lock (FeatureDefinitionManager's
    // pattern). Racing recomputes are benign; unordered publication of a fresh object is not.
    private volatile MergedSnapshot? _snapshot;

    /// <inheritdoc/>
    /// <exception cref="ArgumentNullException"><paramref name="name"/> is <see langword="null"/>.</exception>
    public async Task<SettingDefinition?> FindAsync(string name, CancellationToken cancellationToken = default)
    {
        Argument.IsNotNull(name);

        return await staticStore.GetOrDefaultAsync(name, cancellationToken).ConfigureAwait(false)
            ?? await dynamicStore.GetOrDefaultAsync(name, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<SettingDefinition>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        var staticSettings = await staticStore.GetAllAsync(cancellationToken).ConfigureAwait(false);
        var dynamicSettings = await dynamicStore.GetAllAsync(cancellationToken).ConfigureAwait(false);

        var snapshot = _snapshot;

        if (snapshot?.Matches(staticSettings, dynamicSettings) == true)
        {
            return snapshot.Merged;
        }

        var staticSettingNames = staticSettings.Select(p => p.Name).ToImmutableHashSet(StringComparer.Ordinal);
        // Prefer static settings over dynamics
        var uniqueDynamicSettings = dynamicSettings.Where(d => !staticSettingNames.Contains(d.Name));
        var merged = staticSettings.Concat(uniqueDynamicSettings).ToImmutableList();

        _snapshot = new MergedSnapshot(staticSettings, dynamicSettings, merged);

        return merged;
    }

    /// <summary>
    /// A merged view with its two source references. Both stores hand out immutable snapshots swapped wholesale
    /// on refresh, so reference equality on the sources proves the merge is current; without it the whole
    /// catalog was re-hashed and re-concatenated on every settings batch read.
    /// </summary>
    private sealed class MergedSnapshot(
        IReadOnlyList<SettingDefinition> staticDefinitions,
        IReadOnlyList<SettingDefinition> dynamicDefinitions,
        IReadOnlyList<SettingDefinition> merged
    )
    {
        public IReadOnlyList<SettingDefinition> Merged => merged;

        public bool Matches(
            IReadOnlyList<SettingDefinition> currentStatic,
            IReadOnlyList<SettingDefinition> currentDynamic
        )
        {
            return ReferenceEquals(staticDefinitions, currentStatic)
                && ReferenceEquals(dynamicDefinitions, currentDynamic);
        }
    }
}
