// Copyright (c) Mahmoud Shaheen, 2024. All rights reserved

using Framework.Settings.Models;
using Framework.Settings.ValueProviders;

namespace Framework.Settings.Values;

public static class SettingProviderExtensions
{
    public static async Task<bool> IsTrueAsync(
        this ISettingProvider settingProvider,
        string name,
        string providerName,
        string? providerKey,
        bool fallback = true,
        CancellationToken cancellationToken = default
    )
    {
        var value = await settingProvider.GetOrDefaultAsync(
            name,
            providerName,
            providerKey,
            fallback,
            cancellationToken
        );

        return string.Equals(value, "true", StringComparison.OrdinalIgnoreCase);
    }

    public static async Task<bool> IsFalseAsync(
        this ISettingProvider settingProvider,
        string name,
        string providerName,
        string? providerKey,
        bool fallback = true,
        CancellationToken cancellationToken = default
    )
    {
        var value = await settingProvider.GetOrDefaultAsync(
            name,
            providerName,
            providerKey,
            fallback,
            cancellationToken
        );

        return string.Equals(value, "false", StringComparison.OrdinalIgnoreCase);
    }

    public static async Task<T> GetAsync<T>(
        this ISettingProvider settingProvider,
        string name,
        string providerName,
        string? providerKey,
        bool fallback = true,
        T defaultValue = default,
        CancellationToken cancellationToken = default
    )
        where T : struct
    {
        var value = await settingProvider.GetOrDefaultAsync(
            name,
            providerName,
            providerKey,
            fallback,
            cancellationToken
        );

        return value?.To<T>() ?? defaultValue;
    }
}

public static class DefaultSettingProviderExtensions
{
    public static async Task<bool> IsTrueDefaultAsync(
        this ISettingProvider settingProvider,
        string name,
        bool fallback = true,
        CancellationToken cancellationToken = default
    )
    {
        return await settingProvider.IsTrueAsync(
            name,
            DefaultValueSettingValueProvider.ProviderName,
            providerKey: null,
            fallback,
            cancellationToken
        );
    }

    public static async Task<bool> IsFalseDefaultAsync(
        this ISettingProvider settingProvider,
        string name,
        bool fallback = true,
        CancellationToken cancellationToken = default
    )
    {
        return await settingProvider.IsFalseAsync(
            name,
            DefaultValueSettingValueProvider.ProviderName,
            providerKey: null,
            fallback,
            cancellationToken
        );
    }

    public static Task<T> GetDefaultAsync<T>(
        this ISettingProvider settingProvider,
        string name,
        T defaultValue = default,
        bool fallback = true,
        CancellationToken cancellationToken = default
    )
        where T : struct
    {
        return settingProvider.GetAsync(
            name,
            DefaultValueSettingValueProvider.ProviderName,
            providerKey: null,
            fallback,
            defaultValue,
            cancellationToken
        );
    }

    public static Task<string?> GetOrDefaultDefaultAsync(
        this ISettingProvider settingManager,
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
        this ISettingProvider settingManager,
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

public static class ConfigurationValueSettingProviderExtensions
{
    public static async Task<bool> IsTrueConfigurationAsync(
        this ISettingProvider settingProvider,
        string name,
        bool fallback = true,
        CancellationToken cancellationToken = default
    )
    {
        return await settingProvider.IsTrueAsync(
            name,
            ConfigurationSettingValueProvider.ProviderName,
            providerKey: null,
            fallback,
            cancellationToken
        );
    }

    public static async Task<bool> IsFalseConfigurationAsync(
        this ISettingProvider settingProvider,
        string name,
        bool fallback = true,
        CancellationToken cancellationToken = default
    )
    {
        return await settingProvider.IsFalseAsync(
            name,
            ConfigurationSettingValueProvider.ProviderName,
            providerKey: null,
            fallback,
            cancellationToken
        );
    }

    public static Task<T> GetConfigurationAsync<T>(
        this ISettingProvider settingProvider,
        string name,
        T defaultValue = default,
        bool fallback = true,
        CancellationToken cancellationToken = default
    )
        where T : struct
    {
        return settingProvider.GetAsync(
            name,
            ConfigurationSettingValueProvider.ProviderName,
            providerKey: null,
            fallback,
            defaultValue,
            cancellationToken
        );
    }

    public static Task<string?> GetOrDefaultConfigurationAsync(
        this ISettingProvider settingManager,
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
        this ISettingProvider settingManager,
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

public static class GlobalSettingProviderExtensions
{
    public static async Task<bool> IsTrueGlobalAsync(
        this ISettingProvider settingProvider,
        string name,
        bool fallback = true,
        CancellationToken cancellationToken = default
    )
    {
        return await settingProvider.IsTrueAsync(
            name,
            GlobalSettingValueProvider.ProviderName,
            providerKey: null,
            fallback,
            cancellationToken
        );
    }

    public static async Task<bool> IsFalseGlobalAsync(
        this ISettingProvider settingProvider,
        string name,
        bool fallback = true,
        CancellationToken cancellationToken = default
    )
    {
        return await settingProvider.IsFalseAsync(
            name,
            GlobalSettingValueProvider.ProviderName,
            providerKey: null,
            fallback,
            cancellationToken
        );
    }

    public static Task<T> GetGlobalAsync<T>(
        this ISettingProvider settingProvider,
        string name,
        T defaultValue = default,
        bool fallback = true,
        CancellationToken cancellationToken = default
    )
        where T : struct
    {
        return settingProvider.GetAsync(
            name,
            GlobalSettingValueProvider.ProviderName,
            providerKey: null,
            fallback,
            defaultValue,
            cancellationToken
        );
    }

    public static Task<string?> GetOrDefaultGlobalAsync(
        this ISettingProvider settingManager,
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
        this ISettingProvider settingManager,
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
        this ISettingProvider settingManager,
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

public static class UserSettingProviderExtensions
{
    public static async Task<bool> IsTrueForUserAsync(
        this ISettingProvider settingProvider,
        string name,
        string userId,
        bool fallback = true,
        CancellationToken cancellationToken = default
    )
    {
        return await settingProvider.IsTrueAsync(
            name,
            UserSettingValueProvider.ProviderName,
            userId,
            fallback,
            cancellationToken
        );
    }

    public static async Task<bool> IsTrueForCurrentUserAsync(
        this ISettingProvider settingProvider,
        string name,
        bool fallback = true,
        CancellationToken cancellationToken = default
    )
    {
        return await settingProvider.IsTrueAsync(
            name,
            UserSettingValueProvider.ProviderName,
            providerKey: null,
            fallback,
            cancellationToken
        );
    }

    public static async Task<bool> IsFalseForUserAsync(
        this ISettingProvider settingProvider,
        string name,
        string userId,
        bool fallback = true,
        CancellationToken cancellationToken = default
    )
    {
        return await settingProvider.IsFalseAsync(
            name,
            UserSettingValueProvider.ProviderName,
            userId,
            fallback,
            cancellationToken
        );
    }

    public static async Task<bool> IsFalseForCurrentUserAsync(
        this ISettingProvider settingProvider,
        string name,
        bool fallback = true,
        CancellationToken cancellationToken = default
    )
    {
        return await settingProvider.IsFalseAsync(
            name,
            UserSettingValueProvider.ProviderName,
            providerKey: null,
            fallback,
            cancellationToken
        );
    }

    public static Task<T> GetForUserAsync<T>(
        this ISettingProvider settingProvider,
        string userId,
        string name,
        T defaultValue = default,
        bool fallback = true,
        CancellationToken cancellationToken = default
    )
        where T : struct
    {
        return settingProvider.GetAsync(
            name,
            UserSettingValueProvider.ProviderName,
            userId,
            fallback,
            defaultValue,
            cancellationToken
        );
    }

    public static Task<T> GetForCurrentUserAsync<T>(
        this ISettingProvider settingProvider,
        string name,
        T defaultValue = default,
        bool fallback = true,
        CancellationToken cancellationToken = default
    )
        where T : struct
    {
        return settingProvider.GetAsync(
            name,
            UserSettingValueProvider.ProviderName,
            providerKey: null,
            fallback,
            defaultValue,
            cancellationToken
        );
    }

    public static Task<string?> GetOrDefaultForUserAsync(
        this ISettingProvider settingManager,
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
        this ISettingProvider settingManager,
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
        this ISettingProvider settingManager,
        string userId,
        bool fallback = true,
        CancellationToken cancellationToken = default
    )
    {
        return settingManager.GetAllAsync(UserSettingValueProvider.ProviderName, userId, fallback, cancellationToken);
    }

    public static Task<List<SettingValue>> GetAllForCurrentUserAsync(
        this ISettingProvider settingManager,
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
        this ISettingProvider settingManager,
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
        this ISettingProvider settingManager,
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

public static class TenantSettingProviderExtensions
{
    public static async Task<bool> IsTrueForTenantAsync(
        this ISettingProvider settingProvider,
        string name,
        string tenantId,
        bool fallback = true,
        CancellationToken cancellationToken = default
    )
    {
        return await settingProvider.IsTrueAsync(
            name,
            TenantSettingValueProvider.ProviderName,
            tenantId,
            fallback,
            cancellationToken
        );
    }

    public static async Task<bool> IsTrueForCurrentTenantAsync(
        this ISettingProvider settingProvider,
        string name,
        bool fallback = true,
        CancellationToken cancellationToken = default
    )
    {
        return await settingProvider.IsTrueAsync(
            name,
            TenantSettingValueProvider.ProviderName,
            providerKey: null,
            fallback,
            cancellationToken
        );
    }

    public static async Task<bool> IsFalseForTenantAsync(
        this ISettingProvider settingProvider,
        string name,
        string tenantId,
        bool fallback = true,
        CancellationToken cancellationToken = default
    )
    {
        return await settingProvider.IsFalseAsync(
            name,
            TenantSettingValueProvider.ProviderName,
            tenantId,
            fallback,
            cancellationToken
        );
    }

    public static async Task<bool> IsFalseForCurrentTenantAsync(
        this ISettingProvider settingProvider,
        string name,
        bool fallback = true,
        CancellationToken cancellationToken = default
    )
    {
        return await settingProvider.IsFalseAsync(
            name,
            TenantSettingValueProvider.ProviderName,
            providerKey: null,
            fallback,
            cancellationToken
        );
    }

    public static Task<T> GetForTenantAsync<T>(
        this ISettingProvider settingProvider,
        string tenantId,
        string name,
        T defaultValue = default,
        bool fallback = true,
        CancellationToken cancellationToken = default
    )
        where T : struct
    {
        return settingProvider.GetAsync(
            name,
            TenantSettingValueProvider.ProviderName,
            tenantId,
            fallback,
            defaultValue,
            cancellationToken
        );
    }

    public static Task<T> GetForCurrentTenantAsync<T>(
        this ISettingProvider settingProvider,
        string name,
        T defaultValue = default,
        bool fallback = true,
        CancellationToken cancellationToken = default
    )
        where T : struct
    {
        return settingProvider.GetAsync(
            name,
            TenantSettingValueProvider.ProviderName,
            providerKey: null,
            fallback,
            defaultValue,
            cancellationToken
        );
    }

    public static Task<string?> GetOrDefaultForTenantAsync(
        this ISettingProvider settingManager,
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
        this ISettingProvider settingManager,
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
        this ISettingProvider settingManager,
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
        this ISettingProvider settingManager,
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
        this ISettingProvider settingManager,
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
        this ISettingProvider settingManager,
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
        this ISettingProvider settingManager,
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
