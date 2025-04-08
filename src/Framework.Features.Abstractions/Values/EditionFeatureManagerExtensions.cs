using Framework.Features.Models;

namespace Framework.Features.Values;

[PublicAPI]
public static class EditionFeatureManagerExtensions
{
    public static Task<FeatureValue> GetForEditionAsync(
        this IFeatureManager featureManager,
        string name,
        string editionId,
        bool fallback = true
    )
    {
        return featureManager.GetAsync(name, FeatureValueProviderNames.Edition, editionId, fallback);
    }

    public static Task<List<FeatureValue>> GetAllForEditionAsync(
        this IFeatureManager featureManager,
        string editionId,
        bool fallback = true
    )
    {
        return featureManager.GetAllAsync(FeatureValueProviderNames.Edition, editionId, fallback);
    }

    public static Task DeleteForEditionAsync(
        this IFeatureManager featureManager,
        string editionId,
        CancellationToken cancellationToken = default
    )
    {
        return featureManager.DeleteAsync(FeatureValueProviderNames.Edition, editionId, cancellationToken);
    }

    /// <inheritdoc cref="IFeatureManager.SetAsync"/>
    public static Task SetForEditionAsync(
        this IFeatureManager featureManager,
        string name,
        string? value,
        string editionId,
        bool forceToSet = false
    )
    {
        return featureManager.SetAsync(name, value, FeatureValueProviderNames.Edition, editionId, forceToSet);
    }

    /// <summary>Grant a feature to an edition.</summary>
    public static Task GrantToEditionAsync(this IFeatureManager featureManager, string name, string editionId)
    {
        return featureManager.GrantAsync(name, FeatureValueProviderNames.Edition, editionId);
    }

    /// <summary>Revoke a feature from an edition.</summary>
    public static Task RevokeFromEditionAsync(this IFeatureManager featureManager, string name, string editionId)
    {
        return featureManager.RevokeAsync(name, FeatureValueProviderNames.Edition, editionId);
    }
}
