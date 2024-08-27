using Framework.Settings.DefinitionProviders;

namespace Framework.Settings.DefinitionStores;

/// <summary>Can be implemented to provide the setting definitions from dynamic source.</summary>
public interface IDynamicSettingDefinitionStore
{
    Task<IReadOnlyList<SettingDefinition>> GetAllAsync();

    Task<SettingDefinition?> GetOrDefaultAsync(string name);
}

public sealed class NullDynamicSettingDefinitionStore : IDynamicSettingDefinitionStore
{
    private static readonly Task<SettingDefinition?> _CachedNullableSettingResult = Task.FromResult<SettingDefinition?>(
        null
    );

    private static readonly Task<IReadOnlyList<SettingDefinition>> _CachedSettingsResult = Task.FromResult<
        IReadOnlyList<SettingDefinition>
    >([]);

    public Task<SettingDefinition?> GetOrDefaultAsync(string name)
    {
        return _CachedNullableSettingResult;
    }

    public Task<IReadOnlyList<SettingDefinition>> GetAllAsync()
    {
        return _CachedSettingsResult;
    }
}
