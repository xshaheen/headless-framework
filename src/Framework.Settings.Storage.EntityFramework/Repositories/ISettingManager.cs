using Framework.Kernel.Checks;
using Framework.Settings.Definitions;
using Framework.Settings.Helpers;
using Framework.Settings.Models;
using Framework.Settings.Options;
using Framework.Settings.ValueProviders;
using Framework.Settings.Values;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Framework.Settings.Repositories;

public interface ISettingManager
{
    Task<string?> GetOrDefaultAsync(string name, string providerName, string? providerKey, bool fallback = true);

    Task<List<SettingValue>> GetAllAsync(string providerName, string? providerKey, bool fallback = true);

    Task SetAsync(string name, string? value, string providerName, string? providerKey, bool forceToSet = false);

    Task DeleteAsync(string providerName, string providerKey);
}

public static class DefaultSettingManagerExtensions
{
    public static Task<string?> GetOrNullDefaultAsync(
        this ISettingManager settingManager,
        string name,
        bool fallback = true
    )
    {
        return settingManager.GetOrDefaultAsync(
            name,
            DefaultValueSettingValueProvider.ProviderName,
            providerKey: null,
            fallback
        );
    }

    public static Task<List<SettingValue>> GetAllDefaultAsync(this ISettingManager settingManager, bool fallback = true)
    {
        return settingManager.GetAllAsync(DefaultValueSettingValueProvider.ProviderName, providerKey: null, fallback);
    }
}

public static class ConfigurationValueSettingManagerExtensions
{
    public static Task<string?> GetOrNullConfigurationAsync(
        this ISettingManager settingManager,
        string name,
        bool fallback = true
    )
    {
        return settingManager.GetOrDefaultAsync(name, ConfigurationSettingValueProvider.ProviderName, null, fallback);
    }

    public static Task<List<SettingValue>> GetAllConfigurationAsync(
        this ISettingManager settingManager,
        bool fallback = true
    )
    {
        return settingManager.GetAllAsync(ConfigurationSettingValueProvider.ProviderName, null, fallback);
    }
}

public static class GlobalSettingManagerExtensions
{
    public static Task<string?> GetOrNullGlobalAsync(
        this ISettingManager settingManager,
        string name,
        bool fallback = true
    )
    {
        return settingManager.GetOrDefaultAsync(
            name,
            GlobalSettingValueProvider.ProviderName,
            providerKey: null,
            fallback
        );
    }

    public static Task<List<SettingValue>> GetAllGlobalAsync(this ISettingManager settingManager, bool fallback = true)
    {
        return settingManager.GetAllAsync(GlobalSettingValueProvider.ProviderName, providerKey: null, fallback);
    }

    public static Task SetGlobalAsync(this ISettingManager settingManager, string name, string? value)
    {
        return settingManager.SetAsync(name, value, GlobalSettingValueProvider.ProviderName, providerKey: null);
    }
}

public static class UserSettingManagerExtensions
{
    public static Task<string?> GetOrNullForUserAsync(
        this ISettingManager settingManager,
        string name,
        Guid userId,
        bool fallback = true
    )
    {
        return settingManager.GetOrDefaultAsync(
            name,
            UserSettingValueProvider.ProviderName,
            userId.ToString(),
            fallback
        );
    }

    public static Task<string?> GetOrNullForCurrentUserAsync(
        this ISettingManager settingManager,
        string name,
        bool fallback = true
    )
    {
        return settingManager.GetOrDefaultAsync(name, UserSettingValueProvider.ProviderName, null, fallback);
    }

    public static Task<List<SettingValue>> GetAllForUserAsync(
        this ISettingManager settingManager,
        Guid userId,
        bool fallback = true
    )
    {
        return settingManager.GetAllAsync(UserSettingValueProvider.ProviderName, userId.ToString(), fallback);
    }

    public static Task<List<SettingValue>> GetAllForCurrentUserAsync(
        this ISettingManager settingManager,
        bool fallback = true
    )
    {
        return settingManager.GetAllAsync(UserSettingValueProvider.ProviderName, null, fallback);
    }

    public static Task SetForUserAsync(
        this ISettingManager settingManager,
        Guid userId,
        string name,
        string? value,
        bool forceToSet = false
    )
    {
        return settingManager.SetAsync(
            name,
            value,
            UserSettingValueProvider.ProviderName,
            userId.ToString(),
            forceToSet
        );
    }

    public static Task SetForCurrentUserAsync(
        this ISettingManager settingManager,
        string name,
        string? value,
        bool forceToSet = false
    )
    {
        return settingManager.SetAsync(name, value, UserSettingValueProvider.ProviderName, null, forceToSet);
    }
}

public static class TenantSettingManagerExtensions
{
    public static Task<string?> GetOrNullForTenantAsync(
        this ISettingManager settingManager,
        string name,
        Guid tenantId,
        bool fallback = true
    )
    {
        return settingManager.GetOrDefaultAsync(
            name,
            TenantSettingValueProvider.ProviderName,
            tenantId.ToString(),
            fallback
        );
    }

    public static Task<string?> GetOrNullForCurrentTenantAsync(
        this ISettingManager settingManager,
        string name,
        bool fallback = true
    )
    {
        return settingManager.GetOrDefaultAsync(name, TenantSettingValueProvider.ProviderName, null, fallback);
    }

    public static Task<List<SettingValue>> GetAllForTenantAsync(
        this ISettingManager settingManager,
        Guid tenantId,
        bool fallback = true
    )
    {
        return settingManager.GetAllAsync(TenantSettingValueProvider.ProviderName, tenantId.ToString(), fallback);
    }

    public static Task<List<SettingValue>> GetAllForCurrentTenantAsync(
        this ISettingManager settingManager,
        bool fallback = true
    )
    {
        return settingManager.GetAllAsync(TenantSettingValueProvider.ProviderName, null, fallback);
    }

    public static Task SetForTenantAsync(
        this ISettingManager settingManager,
        Guid tenantId,
        string name,
        string? value,
        bool forceToSet = false
    )
    {
        return settingManager.SetAsync(
            name,
            value,
            TenantSettingValueProvider.ProviderName,
            tenantId.ToString(),
            forceToSet
        );
    }

    public static Task SetForCurrentTenantAsync(
        this ISettingManager settingManager,
        string name,
        string? value,
        bool forceToSet = false
    )
    {
        return settingManager.SetAsync(name, value, TenantSettingValueProvider.ProviderName, null, forceToSet);
    }

    public static Task SetForTenantOrGlobalAsync(
        this ISettingManager settingManager,
        Guid? tenantId,
        string name,
        string? value,
        bool forceToSet = false
    )
    {
        if (tenantId.HasValue)
        {
            return settingManager.SetForTenantAsync(tenantId.Value, name, value, forceToSet);
        }

        return settingManager.SetGlobalAsync(name, value);
    }
}

public sealed class SettingManager : ISettingManager
{
    private readonly ISettingDefinitionManager _settingDefinitionManager;
    private readonly ISettingEncryptionService _settingEncryptionService;
    private readonly ISettingManagementStore _settingManagementStore;
    private readonly IServiceProvider _serviceProvider;
    private readonly SettingManagementOptions _options;
    private readonly Lazy<List<ISettingManagementProvider>> _lazyProviders;

    public SettingManager(
        ISettingDefinitionManager settingDefinitionManager,
        ISettingEncryptionService settingEncryptionService,
        ISettingManagementStore settingManagementStore,
        IServiceProvider serviceProvider,
        IOptions<SettingManagementOptions> options
    )
    {
        _settingDefinitionManager = settingDefinitionManager;
        _settingEncryptionService = settingEncryptionService;
        _settingManagementStore = settingManagementStore;
        _serviceProvider = serviceProvider;
        _options = options.Value;
        _lazyProviders = new(_CreateProviders, isThreadSafe: true);
    }

    public async Task SetAsync(
        string name,
        string? value,
        string providerName,
        string? providerKey,
        bool forceToSet = false
    )
    {
        Argument.IsNotNull(name);
        Argument.IsNotNull(providerName);

        var setting =
            await _settingDefinitionManager.GetOrDefaultAsync(name)
            ?? throw new InvalidOperationException($"Undefined setting: {name}");

        var providers = Enumerable.Reverse(_lazyProviders.Value).SkipWhile(p => p.Name != providerName).ToList();

        if (providers.Count == 0)
        {
            throw new InvalidOperationException($"Unknown setting value provider: {providerName}");
        }

        if (setting.IsEncrypted)
        {
            value = _settingEncryptionService.Encrypt(setting, value);
        }

        if (providers.Count > 1 && !forceToSet && setting.IsInherited && value != null)
        {
            var fallbackValue = await _CoreGetOrDefaultAsync(name, providers[1].Name, null);

            if (string.Equals(fallbackValue, value, StringComparison.Ordinal))
            {
                //Clear the value if it's same as it's fallback value
                value = null;
            }
        }

        providers = providers.TakeWhile(p => p.Name == providerName).ToList(); // Getting list for case of there are more than one provider with same providerName

        if (value == null)
        {
            foreach (var provider in providers)
            {
                await provider.ClearAsync(setting, providerKey);
            }
        }
        else
        {
            foreach (var provider in providers)
            {
                await provider.SetAsync(setting, value, providerKey);
            }
        }
    }

    public Task<string?> GetOrDefaultAsync(string name, string providerName, string? providerKey, bool fallback = true)
    {
        Argument.IsNotNull(name);
        Argument.IsNotNull(providerName);

        return _CoreGetOrDefaultAsync(name, providerName, providerKey, fallback);
    }

    public async Task<List<SettingValue>> GetAllAsync(string providerName, string? providerKey, bool fallback = true)
    {
        Argument.IsNotNull(providerName);

        var settingDefinitions = await _settingDefinitionManager.GetAllAsync();

        var providers = Enumerable
            .Reverse(_lazyProviders.Value)
            .SkipWhile(c => !string.Equals(c.Name, providerName, StringComparison.Ordinal));

        if (!fallback)
        {
            providers = providers.TakeWhile(c => string.Equals(c.Name, providerName, StringComparison.Ordinal));
        }

        var providerList = providers.Reverse().ToList();

        if (providerList.Count == 0)
        {
            return [];
        }

        var settingValues = new Dictionary<string, SettingValue>(StringComparer.Ordinal);

        foreach (var setting in settingDefinitions)
        {
            string? value = null;

            if (setting.IsInherited)
            {
                foreach (var provider in providerList)
                {
                    var providerValue = await provider.GetOrNullAsync(
                        setting,
                        string.Equals(provider.Name, providerName, StringComparison.Ordinal) ? providerKey : null
                    );

                    if (providerValue is not null)
                    {
                        value = providerValue;
                    }
                }
            }
            else
            {
                value = await providerList[0].GetOrNullAsync(setting, providerKey);
            }

            if (setting.IsEncrypted)
            {
                value = _settingEncryptionService.Decrypt(setting, value);
            }

            if (value is not null)
            {
                settingValues[setting.Name] = new SettingValue(setting.Name, value);
            }
        }

        return [.. settingValues.Values];
    }

    public async Task DeleteAsync(string providerName, string providerKey)
    {
        var settings = await _settingManagementStore.GetListAsync(providerName, providerKey);
        foreach (var setting in settings)
        {
            await _settingManagementStore.DeleteAsync(setting.Name, providerName, providerKey);
        }
    }

    #region Helpers

    private async Task<string?> _CoreGetOrDefaultAsync(
        string name,
        string? providerName,
        string? providerKey,
        bool fallback = true
    )
    {
        var definition =
            await _settingDefinitionManager.GetOrDefaultAsync(name)
            ?? throw new InvalidOperationException($"Undefined setting: {name}");

        var valueProviders = Enumerable.Reverse(_lazyProviders.Value);

        if (providerName is not null)
        {
            valueProviders = valueProviders.SkipWhile(c =>
                !string.Equals(c.Name, providerName, StringComparison.Ordinal)
            );
        }

        if (!fallback || !definition.IsInherited)
        {
            valueProviders = valueProviders.TakeWhile(c =>
                string.Equals(c.Name, providerName, StringComparison.Ordinal)
            );
        }

        string? value = null;
        foreach (var provider in valueProviders)
        {
            value = await provider.GetOrNullAsync(
                definition,
                string.Equals(provider.Name, providerName, StringComparison.Ordinal) ? providerKey : null
            );

            if (value != null)
            {
                break;
            }
        }

        if (definition.IsEncrypted)
        {
            value = _settingEncryptionService.Decrypt(definition, value);
        }

        return value;
    }

    private List<ISettingManagementProvider> _CreateProviders()
    {
        using var scope = _serviceProvider.CreateScope();

        return _options
            .Providers.Select(type => (ISettingManagementProvider)scope.ServiceProvider.GetRequiredService(type))
            .ToList();
    }

    #endregion
}
