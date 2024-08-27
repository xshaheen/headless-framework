using Framework.Settings.Definitions;
using Framework.Settings.Values;

namespace Framework.Settings.Providers;

/// <summary>Provides setting values from the default value of the setting definition.</summary>
public sealed class DefaultValueSettingValueProvider(ISettingStore settingStore) : SettingValueProvider(settingStore)
{
    public const string ProviderName = "DefaultValue";

    public override string Name => ProviderName;

    public override Task<string?> GetOrDefaultAsync(SettingDefinition setting)
    {
        return Task.FromResult(setting.DefaultValue);
    }

    public override Task<List<SettingValue>> GetAllAsync(SettingDefinition[] settings)
    {
        var settingValues = settings.Select(x => new SettingValue(x.Name, x.DefaultValue)).ToList();

        return Task.FromResult(settingValues);
    }
}
