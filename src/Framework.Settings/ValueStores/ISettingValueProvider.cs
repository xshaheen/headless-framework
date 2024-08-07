using Framework.Settings.DefinitionProviders;

namespace Framework.Settings.ValueStores;

public interface ISettingValueProvider
{
    string Name { get; }

    Task<string?> GetOrNullAsync(SettingDefinition setting);

    Task<List<SettingValue>> GetAllAsync(SettingDefinition[] settings);
}

public abstract class SettingValueProvider(ISettingStore settingStore) : ISettingValueProvider
{
    public abstract string Name { get; }

    protected ISettingStore SettingStore { get; } = settingStore;

    public abstract Task<string?> GetOrNullAsync(SettingDefinition setting);

    public abstract Task<List<SettingValue>> GetAllAsync(SettingDefinition[] settings);
}
