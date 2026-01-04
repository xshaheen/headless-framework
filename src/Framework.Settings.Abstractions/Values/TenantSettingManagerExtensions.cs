using Framework.Settings.Models;

namespace Framework.Settings.Values;

[PublicAPI]
public static class TenantSettingManagerExtensions
{
    extension(ISettingManager settingManager)
    {
        public async Task<bool> IsTrueForTenantAsync(
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

        public async Task<bool> IsTrueForCurrentTenantAsync(
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

        public async Task<bool> IsFalseForTenantAsync(
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

        public async Task<bool> IsFalseForCurrentTenantAsync(
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

        public Task<T?> FindForTenantAsync<T>(
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

        public Task<T?> FindForCurrentTenantAsync<T>(
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

        public Task<string?> FindForTenantAsync(
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

        public Task<string?> FindForCurrentTenantAsync(
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

        public Task<List<SettingValue>> GetAllForTenantAsync(
            string tenantId,
            bool fallback = true,
            CancellationToken cancellationToken = default
        )
        {
            return settingManager.GetAllAsync(SettingValueProviderNames.Tenant, tenantId, fallback, cancellationToken);
        }

        public Task<List<SettingValue>> GetAllForCurrentTenantAsync(
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

        public Task SetForTenantAsync(
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

        public Task SetForCurrentTenantAsync(
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

        public Task SetForTenantOrGlobalAsync(
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

        public Task SetForTenantAsync<T>(
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

        public Task SetForCurrentTenantAsync<T>(
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

        public Task SetForTenantOrGlobalAsync<T>(
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
}
