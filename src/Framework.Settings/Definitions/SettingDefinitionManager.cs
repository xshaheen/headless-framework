// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Framework.Checks;
using Framework.Settings.Models;

namespace Framework.Settings.Definitions;

/// <summary>
/// Retrieves setting definitions from the static store and falls back
/// to the dynamic store if not found in the static store.
/// </summary>
public interface ISettingDefinitionManager
{
    Task<IReadOnlyList<SettingDefinition>> GetAllAsync(CancellationToken cancellationToken = default);

    Task<SettingDefinition?> GetOrDefaultAsync(string name, CancellationToken cancellationToken = default);
}

public sealed class SettingDefinitionManager(
    IStaticSettingDefinitionStore staticStore,
    IDynamicSettingDefinitionStore dynamicStore
) : ISettingDefinitionManager
{
    public async Task<SettingDefinition?> GetOrDefaultAsync(string name, CancellationToken cancellationToken = default)
    {
        Argument.IsNotNull(name);

        return await staticStore.GetOrDefaultAsync(name, cancellationToken)
            ?? await dynamicStore.GetOrDefaultAsync(name, cancellationToken);
    }

    public async Task<IReadOnlyList<SettingDefinition>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        var staticSettings = await staticStore.GetAllAsync(cancellationToken);
        var staticSettingNames = staticSettings.Select(p => p.Name).ToImmutableHashSet();
        // Prefer static settings over dynamics
        var dynamicSettings = await dynamicStore.GetAllAsync(cancellationToken);
        var uniqueDynamicSettings = dynamicSettings.Where(d => !staticSettingNames.Contains(d.Name));

        return staticSettings.Concat(uniqueDynamicSettings).ToImmutableList();
    }
}
