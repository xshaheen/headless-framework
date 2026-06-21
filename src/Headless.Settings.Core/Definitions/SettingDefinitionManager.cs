// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;
using Headless.Settings.Models;

namespace Headless.Settings.Definitions;

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
        var staticSettingNames = staticSettings.Select(p => p.Name).ToImmutableHashSet();
        // Prefer static settings over dynamics
        var dynamicSettings = await dynamicStore.GetAllAsync(cancellationToken).ConfigureAwait(false);
        var uniqueDynamicSettings = dynamicSettings.Where(d => !staticSettingNames.Contains(d.Name));

        return staticSettings.Concat(uniqueDynamicSettings).ToImmutableList();
    }
}
