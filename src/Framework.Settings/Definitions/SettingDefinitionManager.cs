// Copyright (c) Mahmoud Shaheen, 2024. All rights reserved

using Framework.Kernel.Checks;
using Framework.Settings.Models;

namespace Framework.Settings.Definitions;

/// <summary>Retrieves setting definitions from a service provider and <see cref="SettingManagementProvidersOptions.DefinitionProviders"/></summary>
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
