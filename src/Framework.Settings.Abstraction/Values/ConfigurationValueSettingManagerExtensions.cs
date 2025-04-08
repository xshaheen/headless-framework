using Framework.Settings.Models;

namespace Framework.Settings.Values;

[PublicAPI]
public static class ConfigurationValueSettingManagerExtensions
{
    public static async Task<bool> IsTrueConfigurationAsync(
        this ISettingManager settingManager,
        string name,
        bool fallback = true,
        CancellationToken cancellationToken = default
    )
    {
        return await settingManager.IsTrueAsync(
            name,
            SettingValueProviderNames.Configuration,
            providerKey: null,
            fallback,
            cancellationToken
        );
    }

    public static async Task<bool> IsFalseConfigurationAsync(
        this ISettingManager settingManager,
        string name,
        bool fallback = true,
        CancellationToken cancellationToken = default
    )
    {
        return await settingManager.IsFalseAsync(
            name,
            SettingValueProviderNames.Configuration,
            providerKey: null,
            fallback,
            cancellationToken
        );
    }

    public static Task<T?> GetConfigurationAsync<T>(
        this ISettingManager settingManager,
        string name,
        bool fallback = true,
        CancellationToken cancellationToken = default
    )
    {
        return settingManager.GetAsync<T>(
            name,
            SettingValueProviderNames.Configuration,
            providerKey: null,
            fallback,
            cancellationToken
        );
    }

    public static Task<string?> GetConfigurationAsync(
        this ISettingManager settingManager,
        string name,
        bool fallback = true,
        CancellationToken cancellationToken = default
    )
    {
        return settingManager.GetOrDefaultAsync(
            name,
            SettingValueProviderNames.Configuration,
            providerKey: null,
            fallback,
            cancellationToken
        );
    }

    public static Task<List<SettingValue>> GetAllConfigurationAsync(
        this ISettingManager settingManager,
        bool fallback = true,
        CancellationToken cancellationToken = default
    )
    {
        return settingManager.GetAllAsync(
            SettingValueProviderNames.Configuration,
            providerKey: null,
            fallback,
            cancellationToken
        );
    }
}
