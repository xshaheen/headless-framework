using Framework.Settings.DefinitionProviders;
using Framework.Settings.ValueStores;

namespace Framework.Settings.ValueProviders;

public sealed class GlobalSettingValueProvider(ISettingStore settingStore) : SettingValueProvider(settingStore)
{
    public const string ProviderName = "Global";

    public override string Name => ProviderName;

    public override Task<string?> GetOrNullAsync(SettingDefinition setting)
    {
        return SettingStore.GetOrNullAsync(setting.Name, Name, null);
    }

    public override Task<List<SettingValue>> GetAllAsync(SettingDefinition[] settings)
    {
        return SettingStore.GetAllAsync(settings.Select(x => x.Name).ToArray(), Name, null);
    }
}
