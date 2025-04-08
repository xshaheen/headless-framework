using Framework.Features.Models;

namespace Framework.Features.Values;

[PublicAPI]
public static class TenantFeatureManagerExtensions
{
    public static Task<FeatureValue> GetForTenantAsync(
        this IFeatureManager featureManager,
        string name,
        string tenantId,
        bool fallback = true
    )
    {
        return featureManager.GetAsync(name, FeatureValueProviderNames.Tenant, tenantId, fallback);
    }

    public static Task<List<FeatureValue>> GetAllForTenantAsync(
        this IFeatureManager featureManager,
        string tenantId,
        bool fallback = true
    )
    {
        return featureManager.GetAllAsync(FeatureValueProviderNames.Tenant, tenantId, fallback);
    }

    /// <inheritdoc cref="IFeatureManager.SetAsync"/>
    public static Task SetForTenantAsync(
        this IFeatureManager featureManager,
        string name,
        string? value,
        string tenantId,
        bool forceToSet = false
    )
    {
        return featureManager.SetAsync(name, value, FeatureValueProviderNames.Tenant, tenantId, forceToSet);
    }

    public static Task DeleteForTenantAsync(
        this IFeatureManager featureManager,
        string tenantId,
        CancellationToken cancellationToken = default
    )
    {
        return featureManager.DeleteAsync(FeatureValueProviderNames.Tenant, tenantId, cancellationToken);
    }

    /// <summary>Grant a feature to a tenant.</summary>
    public static Task GrantToTenantAsync(this IFeatureManager featureManager, string name, string tenantId)
    {
        return featureManager.GrantAsync(name, FeatureValueProviderNames.Tenant, tenantId);
    }

    /// <summary>Revoke a feature from a tenant.</summary>
    public static Task RevokeFromTenantAsync(this IFeatureManager featureManager, string name, string tenantId)
    {
        return featureManager.RevokeAsync(name, FeatureValueProviderNames.Tenant, tenantId);
    }
}
