using Framework.Settings.Models;

namespace Framework.Settings.Values;

[PublicAPI]
public static class ConfigurationValueSettingManagerExtensions
{
    public static async Task<bool> IsTrueInConfigurationAsync(
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

    public static async Task<bool> IsFalseInConfigurationAsync(
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

    public static Task<T?> FindInConfigurationAsync<T>(
        this ISettingManager settingManager,
        string name,
        bool fallback = true,
        CancellationToken cancellationToken = default
    )
    {
        return settingManager.FindAsync<T>(
            name,
            SettingValueProviderNames.Configuration,
            providerKey: null,
            fallback,
            cancellationToken
        );
    }

    public static Task<string?> FindInConfigurationAsync(
        this ISettingManager settingManager,
        string name,
        bool fallback = true,
        CancellationToken cancellationToken = default
    )
    {
        return settingManager.FindAsync(
            name,
            SettingValueProviderNames.Configuration,
            providerKey: null,
            fallback,
            cancellationToken
        );
    }

    public static Task<List<SettingValue>> GetAllInConfigurationAsync(
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
