using Framework.Settings.DefinitionProviders;
using Framework.Settings.ValueStores;

namespace Framework.Settings.ValueProviders;

public sealed class DefaultValueSettingValueProvider(ISettingStore settingStore) : SettingValueProvider(settingStore)
{
    public const string ProviderName = "DefaultValue";

    public override string Name => ProviderName;

    public override Task<string?> GetOrNullAsync(SettingDefinition setting)
    {
        return Task.FromResult(setting.DefaultValue);
    }

    public override Task<List<SettingValue>> GetAllAsync(SettingDefinition[] settings)
    {
        var settingValues = settings.Select(x => new SettingValue(x.Name, x.DefaultValue)).ToList();

        return Task.FromResult(settingValues);
    }
}
