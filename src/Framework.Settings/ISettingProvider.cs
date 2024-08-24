using Framework.Settings.DefinitionProviders;
using Framework.Settings.DefinitionStores;
using Framework.Settings.Helpers;
using Framework.Settings.ValueStores;

namespace Framework.Settings;

public interface ISettingProvider
{
    Task<string?> GetOrNullAsync(string name);

    Task<List<SettingValue>> GetAllAsync(string[] names);

    Task<List<SettingValue>> GetAllAsync();
}

public sealed class SettingProvider(
    ISettingDefinitionManager settingDefinitionManager,
    ISettingValueProviderManager settingValueProviderManager,
    ISettingEncryptionService settingEncryptionService
) : ISettingProvider
{
    public async Task<string?> GetOrNullAsync(string name)
    {
        var setting = await settingDefinitionManager.GetOrNullAsync(name);

        if (setting is null)
        {
            return null;
        }

        var providers = Enumerable.Reverse(settingValueProviderManager.Providers);

        if (setting.Providers.Count != 0)
        {
            providers = providers.Where(p => setting.Providers.Contains(p.Name));
        }

        var value = await _GetOrNullValueFromProvidersAsync(providers, setting);
        if (value is not null && setting.IsEncrypted)
        {
            value = settingEncryptionService.Decrypt(setting, value);
        }

        return value;
    }

    public async Task<List<SettingValue>> GetAllAsync(string[] names)
    {
        var settingDefinitions = (await settingDefinitionManager.GetAllAsync())
            .Where(x => names.Contains(x.Name))
            .ToList();

        var result = settingDefinitions.ToDictionary(
            definition => definition.Name,
            definition => new SettingValue(definition.Name)
        );

        foreach (var provider in Enumerable.Reverse(settingValueProviderManager.Providers))
        {
            var settingValues = await provider.GetAllAsync(
                settingDefinitions.Where(x => x.Providers.Count == 0 || x.Providers.Contains(provider.Name)).ToArray()
            );

            var notNullValues = settingValues.Where(x => x.Value is not null).ToList();
            foreach (var settingValue in notNullValues)
            {
                var settingDefinition = settingDefinitions.First(x => x.Name == settingValue.Name);
                if (settingDefinition.IsEncrypted)
                {
                    settingValue.Value = settingEncryptionService.Decrypt(settingDefinition, settingValue.Value);
                }

                if (result.TryGetValue(settingValue.Name, out var value) && value.Value is null)
                {
                    value.Value = settingValue.Value;
                }
            }

            settingDefinitions.RemoveAll(x => notNullValues.Any(v => v.Name == x.Name));

            if (settingDefinitions.Count == 0)
            {
                break;
            }
        }

        return [.. result.Values];
    }

    public async Task<List<SettingValue>> GetAllAsync()
    {
        var settingDefinitions = await settingDefinitionManager.GetAllAsync();
        var settingValues = new List<SettingValue>();

        foreach (var setting in settingDefinitions)
        {
            var value = await GetOrNullAsync(setting.Name);
            settingValues.Add(new SettingValue(setting.Name, value));
        }

        return settingValues;
    }

    private static async Task<string?> _GetOrNullValueFromProvidersAsync(
        IEnumerable<ISettingValueProvider> providers,
        SettingDefinition setting
    )
    {
        foreach (var provider in providers)
        {
            var value = await provider.GetOrNullAsync(setting);

            if (value is not null)
            {
                return value;
            }
        }

        return null;
    }
}
