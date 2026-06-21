// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Settings.Models;

namespace Headless.Settings.Values;

/// <summary>Extensions on <see cref="ISettingManager"/> that scope queries to the <see cref="SettingValueProviderNames.Tenant"/> provider.</summary>
[PublicAPI]
public static class TenantSettingManagerExtensions
{
    extension(ISettingManager settingManager)
    {
        /// <summary>Returns <see langword="true"/> when the named setting value for the specified tenant equals <c>"true"</c> (case-insensitive).</summary>
        /// <param name="tenantId">The identifier of the tenant whose setting is queried.</param>
        /// <param name="name">The unique name of the setting.</param>
        /// <param name="fallback">When <see langword="true"/>, falls back to subsequent providers if no tenant value is found.</param>
        /// <param name="cancellationToken">The abort token.</param>
        /// <returns><see langword="true"/> if the tenant's value is the string <c>"true"</c>; otherwise <see langword="false"/>.</returns>
        public async Task<bool> IsTrueForTenantAsync(
            string tenantId,
            string name,
            bool fallback = true,
            CancellationToken cancellationToken = default
        )
        {
            return await settingManager
                .IsTrueAsync(name, SettingValueProviderNames.Tenant, tenantId, fallback, cancellationToken)
                .ConfigureAwait(false);
        }

        /// <summary>Returns <see langword="true"/> when the named setting value for the ambient current tenant equals <c>"true"</c> (case-insensitive).</summary>
        /// <param name="name">The unique name of the setting.</param>
        /// <param name="fallback">When <see langword="true"/>, falls back to subsequent providers if no value is found for the current tenant.</param>
        /// <param name="cancellationToken">The abort token.</param>
        /// <returns><see langword="true"/> if the current tenant's value is the string <c>"true"</c>; otherwise <see langword="false"/>.</returns>
        public async Task<bool> IsTrueForCurrentTenantAsync(
            string name,
            bool fallback = true,
            CancellationToken cancellationToken = default
        )
        {
            return await settingManager
                .IsTrueAsync(name, SettingValueProviderNames.Tenant, providerKey: null, fallback, cancellationToken)
                .ConfigureAwait(false);
        }

        /// <summary>Returns <see langword="true"/> when the named setting value for the specified tenant equals <c>"false"</c> (case-insensitive).</summary>
        /// <param name="tenantId">The identifier of the tenant whose setting is queried.</param>
        /// <param name="name">The unique name of the setting.</param>
        /// <param name="fallback">When <see langword="true"/>, falls back to subsequent providers if no tenant value is found.</param>
        /// <param name="cancellationToken">The abort token.</param>
        /// <returns><see langword="true"/> if the tenant's value is the string <c>"false"</c>; otherwise <see langword="false"/>.</returns>
        public async Task<bool> IsFalseForTenantAsync(
            string tenantId,
            string name,
            bool fallback = true,
            CancellationToken cancellationToken = default
        )
        {
            return await settingManager
                .IsFalseAsync(name, SettingValueProviderNames.Tenant, tenantId, fallback, cancellationToken)
                .ConfigureAwait(false);
        }

        /// <summary>Returns <see langword="true"/> when the named setting value for the ambient current tenant equals <c>"false"</c> (case-insensitive).</summary>
        /// <param name="name">The unique name of the setting.</param>
        /// <param name="fallback">When <see langword="true"/>, falls back to subsequent providers if no value is found for the current tenant.</param>
        /// <param name="cancellationToken">The abort token.</param>
        /// <returns><see langword="true"/> if the current tenant's value is the string <c>"false"</c>; otherwise <see langword="false"/>.</returns>
        public async Task<bool> IsFalseForCurrentTenantAsync(
            string name,
            bool fallback = true,
            CancellationToken cancellationToken = default
        )
        {
            return await settingManager
                .IsFalseAsync(name, SettingValueProviderNames.Tenant, providerKey: null, fallback, cancellationToken)
                .ConfigureAwait(false);
        }

        /// <summary>Finds the setting value for the specified tenant and deserializes it to <typeparamref name="T"/>.</summary>
        /// <typeparam name="T">The target type to deserialize the stored JSON value into.</typeparam>
        /// <param name="tenantId">The identifier of the tenant whose setting is queried.</param>
        /// <param name="name">The unique name of the setting.</param>
        /// <param name="fallback">When <see langword="true"/>, falls back to subsequent providers if no tenant value is found.</param>
        /// <param name="cancellationToken">The abort token.</param>
        /// <returns>The deserialized value, or <see langword="default"/> when no value is found for the tenant.</returns>
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

        /// <summary>Finds the setting value for the ambient current tenant and deserializes it to <typeparamref name="T"/>.</summary>
        /// <typeparam name="T">The target type to deserialize the stored JSON value into.</typeparam>
        /// <param name="name">The unique name of the setting.</param>
        /// <param name="fallback">When <see langword="true"/>, falls back to subsequent providers if no value is found for the current tenant.</param>
        /// <param name="cancellationToken">The abort token.</param>
        /// <returns>The deserialized value, or <see langword="default"/> when no value is found for the current tenant.</returns>
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

        /// <summary>Returns the raw string setting value for the specified tenant.</summary>
        /// <param name="tenantId">The identifier of the tenant whose setting is queried.</param>
        /// <param name="name">The unique name of the setting.</param>
        /// <param name="fallback">When <see langword="true"/>, falls back to subsequent providers if no tenant value is found.</param>
        /// <param name="cancellationToken">The abort token.</param>
        /// <returns>The setting value string, or <see langword="null"/> if no value is set for the tenant.</returns>
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

        /// <summary>Returns the raw string setting value for the ambient current tenant.</summary>
        /// <param name="name">The unique name of the setting.</param>
        /// <param name="fallback">When <see langword="true"/>, falls back to subsequent providers if no value is found for the current tenant.</param>
        /// <param name="cancellationToken">The abort token.</param>
        /// <returns>The setting value string, or <see langword="null"/> if no value is set for the current tenant.</returns>
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

        /// <summary>Returns all setting values from the <see cref="SettingValueProviderNames.Tenant"/> provider scoped to the specified tenant.</summary>
        /// <param name="tenantId">The identifier of the tenant whose settings are retrieved.</param>
        /// <param name="fallback">When <see langword="true"/>, falls back to subsequent providers for settings without a tenant value.</param>
        /// <param name="cancellationToken">The abort token.</param>
        /// <returns>A list of <see cref="SettingValue"/> instances for the specified tenant.</returns>
        public Task<List<SettingValue>> GetAllForTenantAsync(
            string tenantId,
            bool fallback = true,
            CancellationToken cancellationToken = default
        )
        {
            return settingManager.GetAllAsync(SettingValueProviderNames.Tenant, tenantId, fallback, cancellationToken);
        }

        /// <summary>Returns all setting values from the <see cref="SettingValueProviderNames.Tenant"/> provider scoped to the ambient current tenant.</summary>
        /// <param name="fallback">When <see langword="true"/>, falls back to subsequent providers for settings without a value for the current tenant.</param>
        /// <param name="cancellationToken">The abort token.</param>
        /// <returns>A list of <see cref="SettingValue"/> instances for the current tenant.</returns>
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

        /// <summary>Persists a string setting value scoped to the specified tenant.</summary>
        /// <param name="tenantId">The identifier of the tenant for which the setting is stored.</param>
        /// <param name="name">The unique name of the setting to update.</param>
        /// <param name="value">The value to store, or <see langword="null"/> to clear it.</param>
        /// <param name="forceToSet">When <see langword="true"/>, bypasses provider-level read-only guards.</param>
        /// <param name="cancellationToken">The abort token.</param>
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

        /// <summary>Persists a string setting value scoped to the ambient current tenant.</summary>
        /// <param name="name">The unique name of the setting to update.</param>
        /// <param name="value">The value to store, or <see langword="null"/> to clear it.</param>
        /// <param name="forceToSet">When <see langword="true"/>, bypasses provider-level read-only guards.</param>
        /// <param name="cancellationToken">The abort token.</param>
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

        /// <summary>
        /// Persists a string setting value scoped to the specified tenant, or at the global scope when
        /// <paramref name="tenantId"/> is <see langword="null"/>.
        /// </summary>
        /// <param name="tenantId">The identifier of the tenant, or <see langword="null"/> to write at global scope.</param>
        /// <param name="name">The unique name of the setting to update.</param>
        /// <param name="value">The value to store, or <see langword="null"/> to clear it.</param>
        /// <param name="forceToSet">When <see langword="true"/>, bypasses provider-level read-only guards.</param>
        /// <param name="cancellationToken">The abort token.</param>
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

        /// <summary>Serializes <paramref name="value"/> to JSON and persists it scoped to the specified tenant.</summary>
        /// <typeparam name="T">The type of the value to serialize.</typeparam>
        /// <param name="tenantId">The identifier of the tenant for which the setting is stored.</param>
        /// <param name="name">The unique name of the setting to update.</param>
        /// <param name="value">The value to serialize and store, or <see langword="null"/> to store a JSON null.</param>
        /// <param name="forceToSet">When <see langword="true"/>, bypasses provider-level read-only guards.</param>
        /// <param name="cancellationToken">The abort token.</param>
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

        /// <summary>Serializes <paramref name="value"/> to JSON and persists it scoped to the ambient current tenant.</summary>
        /// <typeparam name="T">The type of the value to serialize.</typeparam>
        /// <param name="name">The unique name of the setting to update.</param>
        /// <param name="value">The value to serialize and store, or <see langword="null"/> to store a JSON null.</param>
        /// <param name="forceToSet">When <see langword="true"/>, bypasses provider-level read-only guards.</param>
        /// <param name="cancellationToken">The abort token.</param>
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

        /// <summary>
        /// Serializes <paramref name="value"/> to JSON and persists it scoped to the specified tenant, or at
        /// global scope when <paramref name="tenantId"/> is <see langword="null"/>.
        /// </summary>
        /// <typeparam name="T">The type of the value to serialize.</typeparam>
        /// <param name="tenantId">The identifier of the tenant, or <see langword="null"/> to write at global scope.</param>
        /// <param name="name">The unique name of the setting to update.</param>
        /// <param name="value">The value to serialize and store, or <see langword="null"/> to store a JSON null.</param>
        /// <param name="forceToSet">When <see langword="true"/>, bypasses provider-level read-only guards.</param>
        /// <param name="cancellationToken">The abort token.</param>
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
