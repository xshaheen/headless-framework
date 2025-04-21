using Framework.Settings.Models;

namespace Framework.Settings.Values;

[PublicAPI]
public static class TenantSettingManagerExtensions
{
    public static async Task<bool> IsTrueForTenantAsync(
        this ISettingManager settingManager,
        string tenantId,
        string name,
        bool fallback = true,
        CancellationToken cancellationToken = default
    )
    {
        return await settingManager.IsTrueAsync(
            name,
            SettingValueProviderNames.Tenant,
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
            SettingValueProviderNames.Tenant,
            providerKey: null,
            fallback,
            cancellationToken
        );
    }

    public static async Task<bool> IsFalseForTenantAsync(
        this ISettingManager settingManager,
        string tenantId,
        string name,
        bool fallback = true,
        CancellationToken cancellationToken = default
    )
    {
        return await settingManager.IsFalseAsync(
            name,
            SettingValueProviderNames.Tenant,
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
            SettingValueProviderNames.Tenant,
            providerKey: null,
            fallback,
            cancellationToken
        );
    }

    public static Task<T?> FindForTenantAsync<T>(
        this ISettingManager settingManager,
        string tenantId,
        string name,
        bool fallback = true,
        CancellationToken cancellationToken = default
    )
    {
        return settingManager.FindAsync<T>(
            name,
            SettingValueProviderNames.Tenant,
            tenantId,
            fallback,
            cancellationToken
        );
    }

    public static Task<T?> FindForCurrentTenantAsync<T>(
        this ISettingManager settingManager,
        string name,
        bool fallback = true,
        CancellationToken cancellationToken = default
    )
    {
        return settingManager.FindAsync<T>(
            name,
            SettingValueProviderNames.Tenant,
            providerKey: null,
            fallback,
            cancellationToken
        );
    }

    public static Task<string?> FindForTenantAsync(
        this ISettingManager settingManager,
        string tenantId,
        string name,
        bool fallback = true,
        CancellationToken cancellationToken = default
    )
    {
        return settingManager.FindAsync(
            name,
            SettingValueProviderNames.Tenant,
            tenantId,
            fallback,
            cancellationToken
        );
    }

    public static Task<string?> FindForCurrentTenantAsync(
        this ISettingManager settingManager,
        string name,
        bool fallback = true,
        CancellationToken cancellationToken = default
    )
    {
        return settingManager.FindAsync(
            name,
            SettingValueProviderNames.Tenant,
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
        return settingManager.GetAllAsync(SettingValueProviderNames.Tenant, tenantId, fallback, cancellationToken);
    }

    public static Task<List<SettingValue>> GetAllForCurrentTenantAsync(
        this ISettingManager settingManager,
        bool fallback = true,
        CancellationToken cancellationToken = default
    )
    {
        return settingManager.GetAllAsync(
            SettingValueProviderNames.Tenant,
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
            SettingValueProviderNames.Tenant,
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
            SettingValueProviderNames.Tenant,
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

    public static Task SetForTenantAsync<T>(
        this ISettingManager settingManager,
        string tenantId,
        string name,
        T? value,
        bool forceToSet = false,
        CancellationToken cancellationToken = default
    )
    {
        return settingManager.SetAsync(
            name,
            value,
            SettingValueProviderNames.Tenant,
            tenantId,
            forceToSet,
            cancellationToken
        );
    }

    public static Task SetForCurrentTenantAsync<T>(
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
            SettingValueProviderNames.Tenant,
            providerKey: null,
            forceToSet,
            cancellationToken
        );
    }

    public static Task SetForTenantOrGlobalAsync<T>(
        this ISettingManager settingManager,
        string? tenantId,
        string name,
        T? value,
        bool forceToSet = false,
        CancellationToken cancellationToken = default
    )
    {
        return tenantId is not null
            ? settingManager.SetForTenantAsync(tenantId, name, value, forceToSet, cancellationToken)
            : settingManager.SetGlobalAsync(name, value, cancellationToken);
    }
}
