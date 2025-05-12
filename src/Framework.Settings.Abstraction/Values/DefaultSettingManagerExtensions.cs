using Framework.Settings.Models;

namespace Framework.Settings.Values;

[PublicAPI]
public static class DefaultSettingManagerExtensions
{
    public static async Task<bool> IsTrueDefaultAsync(
        this ISettingManager settingManager,
        string name,
        bool fallback = true,
        CancellationToken cancellationToken = default
    )
    {
        return await settingManager.IsTrueAsync(
            name,
            SettingValueProviderNames.DefaultValue,
            providerKey: null,
            fallback,
            cancellationToken
        );
    }

    public static async Task<bool> IsFalseDefaultAsync(
        this ISettingManager settingManager,
        string name,
        bool fallback = true,
        CancellationToken cancellationToken = default
    )
    {
        return await settingManager.IsFalseAsync(
            name,
            SettingValueProviderNames.DefaultValue,
            providerKey: null,
            fallback,
            cancellationToken
        );
    }

    public static Task<T?> FindDefaultAsync<T>(
        this ISettingManager settingManager,
        string name,
        bool fallback = true,
        CancellationToken cancellationToken = default
    )
    {
        return settingManager.FindAsync<T>(
            name,
            SettingValueProviderNames.DefaultValue,
            providerKey: null,
            fallback,
            cancellationToken
        );
    }

    public static Task<string?> FindDefaultAsync(
        this ISettingManager settingManager,
        string name,
        bool fallback = true,
        CancellationToken cancellationToken = default
    )
    {
        return settingManager.FindAsync(
            name,
            SettingValueProviderNames.DefaultValue,
            providerKey: null,
            fallback,
            cancellationToken
        );
    }

    public static Task<List<SettingValue>> GetAllDefaultAsync(
        this ISettingManager settingManager,
        bool fallback = true,
        CancellationToken cancellationToken = default
    )
    {
        return settingManager.GetAllAsync(
            SettingValueProviderNames.DefaultValue,
            providerKey: null,
            fallback,
            cancellationToken
        );
    }
}
