using Framework.Features.Models;

namespace Framework.Features.Values;

[PublicAPI]
public static class TenantFeatureManagerExtensions
{
    extension(IFeatureManager featureManager)
    {
        public Task<FeatureValue> GetForTenantAsync(string name, string tenantId, bool fallback = true)
        {
            return featureManager.GetAsync(name, FeatureValueProviderNames.Tenant, tenantId, fallback);
        }

        public Task<List<FeatureValue>> GetAllForTenantAsync(string tenantId, bool fallback = true)
        {
            return featureManager.GetAllAsync(FeatureValueProviderNames.Tenant, tenantId, fallback);
        }

        /// <inheritdoc cref="IFeatureManager.SetAsync"/>
        public Task SetForTenantAsync(string name, string? value, string tenantId, bool forceToSet = false)
        {
            return featureManager.SetAsync(name, value, FeatureValueProviderNames.Tenant, tenantId, forceToSet);
        }

        public Task DeleteForTenantAsync(string tenantId, CancellationToken cancellationToken = default)
        {
            return featureManager.DeleteAsync(FeatureValueProviderNames.Tenant, tenantId, cancellationToken);
        }

        /// <summary>Grant a feature to a tenant.</summary>
        public Task GrantToTenantAsync(string name, string tenantId)
        {
            return featureManager.GrantAsync(name, FeatureValueProviderNames.Tenant, tenantId);
        }

        /// <summary>Revoke a feature from a tenant.</summary>
        public Task RevokeFromTenantAsync(string name, string tenantId)
        {
            return featureManager.RevokeAsync(name, FeatureValueProviderNames.Tenant, tenantId);
        }
    }
}
