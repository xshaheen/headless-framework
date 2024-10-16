// Copyright (c) Mahmoud Shaheen, 2024. All rights reserved

using Framework.Kernel.Checks;
using Framework.Settings.Models;

namespace Framework.Settings.Definitions;

/// <summary>Retrieves setting definitions from a service provider and <see cref="SettingManagementProvidersOptions.DefinitionProviders"/></summary>
public interface ISettingDefinitionManager
{
    Task<IReadOnlyList<SettingDefinition>> GetAllAsync();

    Task<SettingDefinition?> GetOrDefaultAsync(string name);
}

public sealed class SettingDefinitionManager(
    IStaticSettingDefinitionStore staticStore,
    IDynamicSettingDefinitionStore dynamicStore
) : ISettingDefinitionManager
{
    public async Task<SettingDefinition?> GetOrDefaultAsync(string name)
    {
        Argument.IsNotNull(name);

        return await staticStore.GetOrDefaultAsync(name) ?? await dynamicStore.GetOrDefaultAsync(name);
    }

    public async Task<IReadOnlyList<SettingDefinition>> GetAllAsync()
    {
        var staticSettings = await staticStore.GetAllAsync();
        var staticSettingNames = staticSettings.Select(p => p.Name).ToImmutableHashSet();
        // We prefer static settings over dynamics
        var dynamicSettings = await dynamicStore.GetAllAsync();
        var uniqueDynamicSettings = dynamicSettings.Where(d => !staticSettingNames.Contains(d.Name));

        return staticSettings.Concat(uniqueDynamicSettings).ToImmutableList();
    }
}
