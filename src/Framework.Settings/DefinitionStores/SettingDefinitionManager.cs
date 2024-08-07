using Framework.Settings.DefinitionProviders;

namespace Framework.Settings.DefinitionStores;

public interface ISettingDefinitionManager
{
    Task<SettingDefinition?> GetOrNullAsync(string name);

    Task<IReadOnlyList<SettingDefinition>> GetAllAsync();
}

public sealed class SettingDefinitionManager(
    IStaticSettingDefinitionStore staticStore,
    IDynamicSettingDefinitionStore dynamicStore
) : ISettingDefinitionManager
{
    public async Task<SettingDefinition?> GetOrNullAsync(string name)
    {
        return await staticStore.GetOrNullAsync(name) ?? await dynamicStore.GetOrNullAsync(name);
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
