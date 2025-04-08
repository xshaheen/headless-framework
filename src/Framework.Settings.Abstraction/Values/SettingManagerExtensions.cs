// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Framework.Settings.Values;

[PublicAPI]
public static class SettingManagerExtensions
{
    public static async Task<bool> IsTrueAsync(
        this ISettingManager settingManager,
        string name,
        string providerName,
        string? providerKey,
        bool fallback = true,
        CancellationToken cancellationToken = default
    )
    {
        var value = await settingManager.GetOrDefaultAsync(
            name,
            providerName,
            providerKey,
            fallback,
            cancellationToken
        );

        return string.Equals(value, "true", StringComparison.OrdinalIgnoreCase);
    }

    public static async Task<bool> IsFalseAsync(
        this ISettingManager settingManager,
        string name,
        string providerName,
        string? providerKey,
        bool fallback = true,
        CancellationToken cancellationToken = default
    )
    {
        var value = await settingManager.GetOrDefaultAsync(
            name,
            providerName,
            providerKey,
            fallback,
            cancellationToken
        );

        return string.Equals(value, "false", StringComparison.OrdinalIgnoreCase);
    }

    public static async Task<T?> GetAsync<T>(
        this ISettingManager settingManager,
        string name,
        string providerName,
        string? providerKey,
        bool fallback = true,
        CancellationToken cancellationToken = default
    )
    {
        var value = await settingManager.GetOrDefaultAsync(
            name,
            providerName,
            providerKey,
            fallback,
            cancellationToken
        );

        return value.To<T>();
    }
}
