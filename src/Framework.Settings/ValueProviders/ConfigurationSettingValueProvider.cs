using Framework.Settings.DefinitionProviders;
using Framework.Settings.ValueStores;
using Microsoft.Extensions.Configuration;

namespace Framework.Settings.ValueProviders;

public sealed class ConfigurationSettingValueProvider(IConfiguration configuration) : ISettingValueProvider
{
    public const string ConfigurationNamePrefix = "Settings:";
    public const string ProviderName = "Configuration";

    public string Name => ProviderName;

    public Task<string?> GetOrNullAsync(SettingDefinition setting)
    {
        return Task.FromResult(configuration[ConfigurationNamePrefix + setting.Name]);
    }

    public Task<List<SettingValue>> GetAllAsync(SettingDefinition[] settings)
    {
        var settingValues = settings
            .Select(x => new SettingValue(x.Name, configuration[ConfigurationNamePrefix + x.Name]))
            .ToList();

        return Task.FromResult(settingValues);
    }
}
