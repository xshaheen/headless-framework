// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Framework.Serializer;

namespace Framework.Settings.Values;

[PublicAPI]
public static class SettingManagerExtensions
{
    public static async Task<bool> IsTrueAsync(
        this ISettingManager settingManager,
        string settingName,
        string? providerName = null,
        string? providerKey = null,
        bool fallback = true,
        CancellationToken cancellationToken = default
    )
    {
        var value = await settingManager.FindAsync(
            settingName,
            providerName,
            providerKey,
            fallback,
            cancellationToken
        );

        return string.Equals(value, "true", StringComparison.OrdinalIgnoreCase);
    }

    public static async Task<bool> IsFalseAsync(
        this ISettingManager settingManager,
        string settingName,
        string? providerName = null,
        string? providerKey = null,
        bool fallback = true,
        CancellationToken cancellationToken = default
    )
    {
        var value = await settingManager.FindAsync(
            settingName,
            providerName,
            providerKey,
            fallback,
            cancellationToken
        );

        return string.Equals(value, "false", StringComparison.OrdinalIgnoreCase);
    }

    public static async Task<T?> FindAsync<T>(
        this ISettingManager settingManager,
        string settingName,
        string? providerName = null,
        string? providerKey = null,
        bool fallback = true,
        CancellationToken cancellationToken = default
    )
    {
        var value = await settingManager.FindAsync(
            settingName,
            providerName,
            providerKey,
            fallback,
            cancellationToken
        );

        return string.IsNullOrEmpty(value) ? default : JsonSerializer.Deserialize<T>(value, JsonConstants.DefaultInternalJsonOptions);
    }

    public static Task SetAsync<T>(
        this ISettingManager settingManager,
        string settingName,
        T? value,
        string providerName,
        string? providerKey,
        bool forceToSet = false,
        CancellationToken cancellationToken = default
    )
    {
        var valueJson = JsonSerializer.Serialize(value, JsonConstants.DefaultInternalJsonOptions);

        return settingManager.SetAsync(settingName, valueJson, providerName, providerKey, forceToSet, cancellationToken);
    }
}
