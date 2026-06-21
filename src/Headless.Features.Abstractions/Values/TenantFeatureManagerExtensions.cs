// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Features.Models;

namespace Headless.Features.Values;

/// <summary>Extension members on <see cref="IFeatureManager"/> scoped to the tenant provider.</summary>
[PublicAPI]
public static class TenantFeatureManagerExtensions
{
    extension(IFeatureManager featureManager)
    {
        /// <summary>Gets the tenant-scoped value of the feature with the given <paramref name="name"/>.</summary>
        /// <param name="name">The feature name.</param>
        /// <param name="tenantId">The tenant identifier used as the provider key.</param>
        /// <param name="fallback">When <see langword="true"/>, falls back to other providers if the tenant provider has no value.</param>
        /// <returns>A <see cref="FeatureValue"/> from the <c>Tenant</c> provider.</returns>
        public Task<FeatureValue> GetForTenantAsync(string name, string tenantId, bool fallback = true)
        {
            return featureManager.GetAsync(name, FeatureValueProviderNames.Tenant, tenantId, fallback);
        }

        /// <summary>Gets all feature values scoped to the given tenant.</summary>
        /// <param name="tenantId">The tenant identifier used as the provider key.</param>
        /// <param name="fallback">When <see langword="true"/>, falls back to other providers when the tenant provider has no value.</param>
        /// <returns>A list of <see cref="FeatureValue"/> instances for the given tenant.</returns>
        public Task<List<FeatureValue>> GetAllForTenantAsync(string tenantId, bool fallback = true)
        {
            return featureManager.GetAllAsync(FeatureValueProviderNames.Tenant, tenantId, fallback);
        }

        /// <summary>Sets the value of a feature for the given tenant.</summary>
        /// <param name="name">The feature name.</param>
        /// <param name="value">The value to store, or <see langword="null"/> to clear it.</param>
        /// <param name="tenantId">The tenant identifier used as the provider key.</param>
        /// <param name="forceToSet">When <see langword="false"/> and <paramref name="value"/> matches the fallback value, the write is skipped.</param>
        public Task SetForTenantAsync(string name, string? value, string tenantId, bool forceToSet = false)
        {
            return featureManager.SetAsync(name, value, FeatureValueProviderNames.Tenant, tenantId, forceToSet);
        }

        /// <summary>Deletes all feature values stored for the given tenant.</summary>
        /// <param name="tenantId">The tenant identifier whose feature values should be deleted.</param>
        /// <param name="cancellationToken">The abort token.</param>
        public Task DeleteForTenantAsync(string tenantId, CancellationToken cancellationToken = default)
        {
            return featureManager.DeleteAsync(FeatureValueProviderNames.Tenant, tenantId, cancellationToken);
        }

        /// <summary>Grants a feature to a tenant by setting its value to <see langword="true"/>.</summary>
        /// <param name="name">The feature name.</param>
        /// <param name="tenantId">The tenant identifier to grant the feature to.</param>
        /// <exception cref="ArgumentNullException"><paramref name="name"/> is <see langword="null"/>.</exception>
        /// <exception cref="Headless.Exceptions.ConflictException">The feature is not defined or the Tenant provider is read-only.</exception>
        public Task GrantToTenantAsync(string name, string tenantId)
        {
            return featureManager.GrantAsync(name, FeatureValueProviderNames.Tenant, tenantId);
        }

        /// <summary>Revokes a feature from a tenant by setting its value to <see langword="false"/>.</summary>
        /// <param name="name">The feature name.</param>
        /// <param name="tenantId">The tenant identifier to revoke the feature from.</param>
        /// <exception cref="ArgumentNullException"><paramref name="name"/> is <see langword="null"/>.</exception>
        /// <exception cref="Headless.Exceptions.ConflictException">The feature is not defined or the Tenant provider is read-only.</exception>
        public Task RevokeFromTenantAsync(string name, string tenantId)
        {
            return featureManager.RevokeAsync(name, FeatureValueProviderNames.Tenant, tenantId);
        }
    }
}
