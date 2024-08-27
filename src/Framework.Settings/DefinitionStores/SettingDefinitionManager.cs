using Framework.Settings.DefinitionProviders;

namespace Framework.Settings.DefinitionStores;

/// <summary>Manage setting definitions.</summary>
public interface ISettingDefinitionManager
{
    Task<SettingDefinition?> GetOrDefaultAsync(string name);

    Task<IReadOnlyList<SettingDefinition>> GetAllAsync();
}

/// <inheritdoc />
public sealed class SettingDefinitionManager(
    IStaticSettingDefinitionStore staticStore,
    IDynamicSettingDefinitionStore dynamicStore
) : ISettingDefinitionManager
{
    public async Task<SettingDefinition?> GetOrDefaultAsync(string name)
    {
        return await staticStore.GetOrDefaultAsync(name) ?? await dynamicStore.GetOrDefaultAsync(name);
    }

    public async Task<IReadOnlyList<SettingDefinition>> GetAllAsync()
    {
        var staticSettings = await staticStore.GetAllAsync();
        var dynamicSettings = await dynamicStore.GetAllAsync();

        var staticSettingNames = staticSettings.Select(p => p.Name).ToImmutableHashSet();

        /* We prefer static settings over dynamics */
        return staticSettings
            .Concat(dynamicSettings.Where(d => !staticSettingNames.Contains(d.Name)))
            .ToImmutableList();
    }
}
