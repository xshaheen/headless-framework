using Framework.Settings.Models;

namespace Framework.Settings.Values;

[PublicAPI]
public static class UserSettingManagerExtensions
{
    public static async Task<bool> IsTrueForUserAsync(
        this ISettingManager settingManager,
        string userId,
        string name,
        bool fallback = true,
        CancellationToken cancellationToken = default
    )
    {
        return await settingManager.IsTrueAsync(
            name,
            SettingValueProviderNames.User,
            userId,
            fallback,
            cancellationToken
        );
    }

    public static async Task<bool> IsTrueForCurrentUserAsync(
        this ISettingManager settingManager,
        string name,
        bool fallback = true,
        CancellationToken cancellationToken = default
    )
    {
        return await settingManager.IsTrueAsync(
            name,
            SettingValueProviderNames.User,
            providerKey: null,
            fallback,
            cancellationToken
        );
    }

    public static async Task<bool> IsFalseForUserAsync(
        this ISettingManager settingManager,
        string userId,
        string name,
        bool fallback = true,
        CancellationToken cancellationToken = default
    )
    {
        return await settingManager.IsFalseAsync(
            name,
            SettingValueProviderNames.User,
            userId,
            fallback,
            cancellationToken
        );
    }

    public static async Task<bool> IsFalseForCurrentUserAsync(
        this ISettingManager settingManager,
        string name,
        bool fallback = true,
        CancellationToken cancellationToken = default
    )
    {
        return await settingManager.IsFalseAsync(
            name,
            SettingValueProviderNames.User,
            providerKey: null,
            fallback,
            cancellationToken
        );
    }

    public static Task<T?> FindForUserAsync<T>(
        this ISettingManager settingManager,
        string userId,
        string name,
        bool fallback = true,
        CancellationToken cancellationToken = default
    )
    {
        return settingManager.FindAsync<T>(name, SettingValueProviderNames.User, userId, fallback, cancellationToken);
    }

    public static Task<T?> FindForCurrentUserAsync<T>(
        this ISettingManager settingManager,
        string name,
        bool fallback = true,
        CancellationToken cancellationToken = default
    )
    {
        return settingManager.FindAsync<T>(
            name,
            SettingValueProviderNames.User,
            providerKey: null,
            fallback,
            cancellationToken
        );
    }

    public static Task<string?> FindForUserAsync(
        this ISettingManager settingManager,
        string userId,
        string name,
        bool fallback = true,
        CancellationToken cancellationToken = default
    )
    {
        return settingManager.FindAsync(
            name,
            SettingValueProviderNames.User,
            userId,
            fallback,
            cancellationToken
        );
    }

    public static Task<string?> FindForCurrentUserAsync(
        this ISettingManager settingManager,
        string name,
        bool fallback = true,
        CancellationToken cancellationToken = default
    )
    {
        return settingManager.FindAsync(
            name,
            SettingValueProviderNames.User,
            providerKey: null,
            fallback,
            cancellationToken
        );
    }

    public static Task<List<SettingValue>> GetAllForUserAsync(
        this ISettingManager settingManager,
        string userId,
        bool fallback = true,
        CancellationToken cancellationToken = default
    )
    {
        return settingManager.GetAllAsync(SettingValueProviderNames.User, userId, fallback, cancellationToken);
    }

    public static Task<List<SettingValue>> GetAllForCurrentUserAsync(
        this ISettingManager settingManager,
        bool fallback = true,
        CancellationToken cancellationToken = default
    )
    {
        return settingManager.GetAllAsync(
            SettingValueProviderNames.User,
            providerKey: null,
            fallback,
            cancellationToken
        );
    }

    public static Task SetForUserAsync(
        this ISettingManager settingManager,
        string userId,
        string name,
        string? value,
        bool forceToSet = false,
        CancellationToken cancellationToken = default
    )
    {
        return settingManager.SetAsync(
            name,
            value,
            SettingValueProviderNames.User,
            userId,
            forceToSet,
            cancellationToken
        );
    }

    public static Task SetForCurrentUserAsync(
        this ISettingManager settingManager,
        string name,
        string? value,
        bool forceToSet = false,
        CancellationToken cancellationToken = default
    )
    {
        return settingManager.SetAsync(
            name,
            value,
            SettingValueProviderNames.User,
            providerKey: null,
            forceToSet,
            cancellationToken
        );
    }

    public static Task SetForUserAsync<T>(
        this ISettingManager settingManager,
        string userId,
        string name,
        T? value,
        bool forceToSet = false,
        CancellationToken cancellationToken = default
    )
    {
        return settingManager.SetAsync(
            name,
            value,
            SettingValueProviderNames.User,
            userId,
            forceToSet,
            cancellationToken
        );
    }

    public static Task SetForCurrentUserAsync<T>(
        this ISettingManager settingManager,
        string name,
        T? value,
        bool forceToSet = false,
        CancellationToken cancellationToken = default
    )
    {
        return settingManager.SetAsync(
            name,
            value,
            SettingValueProviderNames.User,
            providerKey: null,
            forceToSet,
            cancellationToken
        );
    }
}
