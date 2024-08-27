using Framework.Settings.Definitions;
using Framework.Settings.Values;
using Microsoft.Extensions.Configuration;

namespace Framework.Settings.Providers;

/// <summary>Provides setting values from the <see cref="IConfiguration"/> with prefix <see cref="ConfigurationNamePrefix"/>.</summary>
public sealed class ConfigurationSettingValueProvider(IConfiguration configuration) : ISettingValueProvider
{
    public const string ConfigurationNamePrefix = "Settings:";
    public const string ProviderName = "Configuration";

    public string Name => ProviderName;

    public Task<string?> GetOrDefaultAsync(SettingDefinition setting)
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
