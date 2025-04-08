using Framework.Settings.Models;

namespace Framework.Settings.Values;

[PublicAPI]
public static class GlobalSettingManagerExtensions
{
    public static async Task<bool> IsTrueGlobalAsync(
        this ISettingManager settingManager,
        string name,
        bool fallback = true,
        CancellationToken cancellationToken = default
    )
    {
        return await settingManager.IsTrueAsync(
            name,
            SettingValueProviderNames.Global,
            providerKey: null,
            fallback,
            cancellationToken
        );
    }

    public static async Task<bool> IsFalseGlobalAsync(
        this ISettingManager settingManager,
        string name,
        bool fallback = true,
        CancellationToken cancellationToken = default
    )
    {
        return await settingManager.IsFalseAsync(
            name,
            SettingValueProviderNames.Global,
            providerKey: null,
            fallback,
            cancellationToken
        );
    }

    public static Task<T?> GetGlobalAsync<T>(
        this ISettingManager settingManager,
        string name,
        bool fallback = true,
        CancellationToken cancellationToken = default
    )
    {
        return settingManager.GetAsync<T>(
            name,
            SettingValueProviderNames.Global,
            providerKey: null,
            fallback,
            cancellationToken
        );
    }

    public static Task<string?> GetGlobalAsync(
        this ISettingManager settingManager,
        string name,
        bool fallback = true,
        CancellationToken cancellationToken = default
    )
    {
        return settingManager.GetOrDefaultAsync(
            name,
            SettingValueProviderNames.Global,
            providerKey: null,
            fallback,
            cancellationToken
        );
    }

    public static Task<List<SettingValue>> GetAllGlobalAsync(
        this ISettingManager settingManager,
        bool fallback = true,
        CancellationToken cancellationToken = default
    )
    {
        return settingManager.GetAllAsync(
            SettingValueProviderNames.Global,
            providerKey: null,
            fallback,
            cancellationToken
        );
    }

    public static Task SetGlobalAsync(
        this ISettingManager settingManager,
        string name,
        string? value,
        CancellationToken cancellationToken = default
    )
    {
        return settingManager.SetAsync(
            name,
            value,
            SettingValueProviderNames.Global,
            providerKey: null,
            cancellationToken: cancellationToken
        );
    }
}
