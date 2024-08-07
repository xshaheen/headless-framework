using Framework.Settings.DefinitionProviders;

namespace Framework.Settings.DefinitionStores;

public interface IDynamicSettingDefinitionStore
{
    Task<IReadOnlyList<SettingDefinition>> GetAllAsync();

    Task<SettingDefinition?> GetOrNullAsync(string name);
}

public sealed class NullDynamicSettingDefinitionStore : IDynamicSettingDefinitionStore
{
    private static readonly Task<SettingDefinition?> _CachedNullableSettingResult = Task.FromResult<SettingDefinition?>(
        null
    );

    private static readonly Task<IReadOnlyList<SettingDefinition>> _CachedSettingsResult = Task.FromResult<
        IReadOnlyList<SettingDefinition>
    >([]);

    public Task<SettingDefinition?> GetOrNullAsync(string name)
    {
        return _CachedNullableSettingResult;
    }

    public Task<IReadOnlyList<SettingDefinition>> GetAllAsync()
    {
        return _CachedSettingsResult;
    }
}
