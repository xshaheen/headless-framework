// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Framework.Settings.Models;
using Framework.Settings.ValueProviders;

namespace Framework.Settings.Values;

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

    public static async Task<T> GetAsync<T>(
        this ISettingManager settingManager,
        string name,
        string providerName,
        string? providerKey,
        bool fallback = true,
        T defaultValue = default,
        CancellationToken cancellationToken = default
    )
        where T : struct
    {
        var value = await settingManager.GetOrDefaultAsync(
            name,
            providerName,
            providerKey,
            fallback,
            cancellationToken
        );

        return value?.To<T>() ?? defaultValue;
    }
}

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
            DefaultValueSettingValueProvider.ProviderName,
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
            DefaultValueSettingValueProvider.ProviderName,
            providerKey: null,
            fallback,
            cancellationToken
        );
    }

    public static Task<T> GetDefaultAsync<T>(
        this ISettingManager settingManager,
        string name,
        T defaultValue = default,
        bool fallback = true,
        CancellationToken cancellationToken = default
    )
        where T : struct
    {
        return settingManager.GetAsync(
            name,
            DefaultValueSettingValueProvider.ProviderName,
            providerKey: null,
            fallback,
            defaultValue,
            cancellationToken
        );
    }

    public static Task<string?> GetOrDefaultDefaultAsync(
        this ISettingManager settingManager,
        string name,
        bool fallback = true,
        CancellationToken cancellationToken = default
    )
    {
        return settingManager.GetOrDefaultAsync(
            name,
            DefaultValueSettingValueProvider.ProviderName,
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
            DefaultValueSettingValueProvider.ProviderName,
            providerKey: null,
            fallback,
            cancellationToken
        );
    }
}

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
            ConfigurationSettingValueProvider.ProviderName,
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
            ConfigurationSettingValueProvider.ProviderName,
            providerKey: null,
            fallback,
            cancellationToken
        );
    }

    public static Task<T> GetConfigurationAsync<T>(
        this ISettingManager settingManager,
        string name,
        T defaultValue = default,
        bool fallback = true,
        CancellationToken cancellationToken = default
    )
        where T : struct
    {
        return settingManager.GetAsync(
            name,
            ConfigurationSettingValueProvider.ProviderName,
            providerKey: null,
            fallback,
            defaultValue,
            cancellationToken
        );
    }

    public static Task<string?> GetOrDefaultConfigurationAsync(
        this ISettingManager settingManager,
        string name,
        bool fallback = true,
        CancellationToken cancellationToken = default
    )
    {
        return settingManager.GetOrDefaultAsync(
            name,
            ConfigurationSettingValueProvider.ProviderName,
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
            ConfigurationSettingValueProvider.ProviderName,
            providerKey: null,
            fallback,
            cancellationToken
        );
    }
}

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
            GlobalSettingValueProvider.ProviderName,
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
            GlobalSettingValueProvider.ProviderName,
            providerKey: null,
            fallback,
            cancellationToken
        );
    }

    public static Task<T> GetGlobalAsync<T>(
        this ISettingManager settingManager,
        string name,
        T defaultValue = default,
        bool fallback = true,
        CancellationToken cancellationToken = default
    )
        where T : struct
    {
        return settingManager.GetAsync(
            name,
            GlobalSettingValueProvider.ProviderName,
            providerKey: null,
            fallback,
            defaultValue,
            cancellationToken
        );
    }

    public static Task<string?> GetOrDefaultGlobalAsync(
        this ISettingManager settingManager,
        string name,
        bool fallback = true,
        CancellationToken cancellationToken = default
    )
    {
        return settingManager.GetOrDefaultAsync(
            name,
            GlobalSettingValueProvider.ProviderName,
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
            GlobalSettingValueProvider.ProviderName,
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
            GlobalSettingValueProvider.ProviderName,
            providerKey: null,
            cancellationToken: cancellationToken
        );
    }
}

public static class UserSettingManagerExtensions
{
    public static async Task<bool> IsTrueForUserAsync(
        this ISettingManager settingManager,
        string name,
        string userId,
        bool fallback = true,
        CancellationToken cancellationToken = default
    )
    {
        return await settingManager.IsTrueAsync(
            name,
            UserSettingValueProvider.ProviderName,
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
            UserSettingValueProvider.ProviderName,
            providerKey: null,
            fallback,
            cancellationToken
        );
    }

    public static async Task<bool> IsFalseForUserAsync(
        this ISettingManager settingManager,
        string name,
        string userId,
        bool fallback = true,
        CancellationToken cancellationToken = default
    )
    {
        return await settingManager.IsFalseAsync(
            name,
            UserSettingValueProvider.ProviderName,
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
            UserSettingValueProvider.ProviderName,
            providerKey: null,
            fallback,
            cancellationToken
        );
    }

    public static Task<T> GetForUserAsync<T>(
        this ISettingManager settingManager,
        string userId,
        string name,
        T defaultValue = default,
        bool fallback = true,
        CancellationToken cancellationToken = default
    )
        where T : struct
    {
        return settingManager.GetAsync(
            name,
            UserSettingValueProvider.ProviderName,
            userId,
            fallback,
            defaultValue,
            cancellationToken
        );
    }

    public static Task<T> GetForCurrentUserAsync<T>(
        this ISettingManager settingManager,
        string name,
        T defaultValue = default,
        bool fallback = true,
        CancellationToken cancellationToken = default
    )
        where T : struct
    {
        return settingManager.GetAsync(
            name,
            UserSettingValueProvider.ProviderName,
            providerKey: null,
            fallback,
            defaultValue,
            cancellationToken
        );
    }

    public static Task<string?> GetOrDefaultForUserAsync(
        this ISettingManager settingManager,
        string name,
        string userId,
        bool fallback = true,
        CancellationToken cancellationToken = default
    )
    {
        return settingManager.GetOrDefaultAsync(
            name,
            UserSettingValueProvider.ProviderName,
            userId,
            fallback,
            cancellationToken
        );
    }

    public static Task<string?> GetOrDefaultForCurrentUserAsync(
        this ISettingManager settingManager,
        string name,
        bool fallback = true,
        CancellationToken cancellationToken = default
    )
    {
        return settingManager.GetOrDefaultAsync(
            name,
            UserSettingValueProvider.ProviderName,
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
        return settingManager.GetAllAsync(UserSettingValueProvider.ProviderName, userId, fallback, cancellationToken);
    }

    public static Task<List<SettingValue>> GetAllForCurrentUserAsync(
        this ISettingManager settingManager,
        bool fallback = true,
        CancellationToken cancellationToken = default
    )
    {
        return settingManager.GetAllAsync(
            UserSettingValueProvider.ProviderName,
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
            UserSettingValueProvider.ProviderName,
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
            UserSettingValueProvider.ProviderName,
            providerKey: null,
            forceToSet,
            cancellationToken
        );
    }
}

public static class TenantSettingManagerExtensions
{
    public static async Task<bool> IsTrueForTenantAsync(
        this ISettingManager settingManager,
        string name,
        string tenantId,
        bool fallback = true,
        CancellationToken cancellationToken = default
    )
    {
        return await settingManager.IsTrueAsync(
            name,
            TenantSettingValueProvider.ProviderName,
            tenantId,
            fallback,
            cancellationToken
        );
    }

    public static async Task<bool> IsTrueForCurrentTenantAsync(
        this ISettingManager settingManager,
        string name,
        bool fallback = true,
        CancellationToken cancellationToken = default
    )
    {
        return await settingManager.IsTrueAsync(
            name,
            TenantSettingValueProvider.ProviderName,
            providerKey: null,
            fallback,
            cancellationToken
        );
    }

    public static async Task<bool> IsFalseForTenantAsync(
        this ISettingManager settingManager,
        string name,
        string tenantId,
        bool fallback = true,
        CancellationToken cancellationToken = default
    )
    {
        return await settingManager.IsFalseAsync(
            name,
            TenantSettingValueProvider.ProviderName,
            tenantId,
            fallback,
            cancellationToken
        );
    }

    public static async Task<bool> IsFalseForCurrentTenantAsync(
        this ISettingManager settingManager,
        string name,
        bool fallback = true,
        CancellationToken cancellationToken = default
    )
    {
        return await settingManager.IsFalseAsync(
            name,
            TenantSettingValueProvider.ProviderName,
            providerKey: null,
            fallback,
            cancellationToken
        );
    }

    public static Task<T> GetForTenantAsync<T>(
        this ISettingManager settingManager,
        string tenantId,
        string name,
        T defaultValue = default,
        bool fallback = true,
        CancellationToken cancellationToken = default
    )
        where T : struct
    {
        return settingManager.GetAsync(
            name,
            TenantSettingValueProvider.ProviderName,
            tenantId,
            fallback,
            defaultValue,
            cancellationToken
        );
    }

    public static Task<T> GetForCurrentTenantAsync<T>(
        this ISettingManager settingManager,
        string name,
        T defaultValue = default,
        bool fallback = true,
        CancellationToken cancellationToken = default
    )
        where T : struct
    {
        return settingManager.GetAsync(
            name,
            TenantSettingValueProvider.ProviderName,
            providerKey: null,
            fallback,
            defaultValue,
            cancellationToken
        );
    }

    public static Task<string?> GetOrDefaultForTenantAsync(
        this ISettingManager settingManager,
        string name,
        string tenantId,
        bool fallback = true,
        CancellationToken cancellationToken = default
    )
    {
        return settingManager.GetOrDefaultAsync(
            name,
            TenantSettingValueProvider.ProviderName,
            tenantId,
            fallback,
            cancellationToken
        );
    }

    public static Task<string?> GetOrDefaultForCurrentTenantAsync(
        this ISettingManager settingManager,
        string name,
        bool fallback = true,
        CancellationToken cancellationToken = default
    )
    {
        return settingManager.GetOrDefaultAsync(
            name,
            TenantSettingValueProvider.ProviderName,
            providerKey: null,
            fallback,
            cancellationToken
        );
    }

    public static Task<List<SettingValue>> GetAllForTenantAsync(
        this ISettingManager settingManager,
        string tenantId,
        bool fallback = true,
        CancellationToken cancellationToken = default
    )
    {
        return settingManager.GetAllAsync(
            TenantSettingValueProvider.ProviderName,
            tenantId,
            fallback,
            cancellationToken
        );
    }

    public static Task<List<SettingValue>> GetAllForCurrentTenantAsync(
        this ISettingManager settingManager,
        bool fallback = true,
        CancellationToken cancellationToken = default
    )
    {
        return settingManager.GetAllAsync(
            TenantSettingValueProvider.ProviderName,
            providerKey: null,
            fallback,
            cancellationToken
        );
    }

    public static Task SetForTenantAsync(
        this ISettingManager settingManager,
        string tenantId,
        string name,
        string? value,
        bool forceToSet = false,
        CancellationToken cancellationToken = default
    )
    {
        return settingManager.SetAsync(
            name,
            value,
            TenantSettingValueProvider.ProviderName,
            tenantId,
            forceToSet,
            cancellationToken
        );
    }

    public static Task SetForCurrentTenantAsync(
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
            TenantSettingValueProvider.ProviderName,
            providerKey: null,
            forceToSet,
            cancellationToken
        );
    }

    public static Task SetForTenantOrGlobalAsync(
        this ISettingManager settingManager,
        string? tenantId,
        string name,
        string? value,
        bool forceToSet = false,
        CancellationToken cancellationToken = default
    )
    {
        return tenantId is not null
            ? settingManager.SetForTenantAsync(tenantId, name, value, forceToSet, cancellationToken)
            : settingManager.SetGlobalAsync(name, value, cancellationToken);
    }
}
