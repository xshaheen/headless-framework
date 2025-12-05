using Framework.Features.Models;

namespace Framework.Features.Values;

[PublicAPI]
public static class EditionFeatureManagerExtensions
{
    extension(IFeatureManager featureManager)
    {
        public Task<FeatureValue> GetForEditionAsync(string name, string editionId, bool fallback = true)
        {
            return featureManager.GetAsync(name, FeatureValueProviderNames.Edition, editionId, fallback);
        }

        public Task<List<FeatureValue>> GetAllForEditionAsync(string editionId, bool fallback = true)
        {
            return featureManager.GetAllAsync(FeatureValueProviderNames.Edition, editionId, fallback);
        }

        public Task DeleteForEditionAsync(string editionId, CancellationToken cancellationToken = default)
        {
            return featureManager.DeleteAsync(FeatureValueProviderNames.Edition, editionId, cancellationToken);
        }

        /// <inheritdoc cref="IFeatureManager.SetAsync"/>
        public Task SetForEditionAsync(string name, string? value, string editionId, bool forceToSet = false)
        {
            return featureManager.SetAsync(name, value, FeatureValueProviderNames.Edition, editionId, forceToSet);
        }

        /// <summary>Grant a feature to an edition.</summary>
        public Task GrantToEditionAsync(string name, string editionId)
        {
            return featureManager.GrantAsync(name, FeatureValueProviderNames.Edition, editionId);
        }

        /// <summary>Revoke a feature from an edition.</summary>
        public Task RevokeFromEditionAsync(string name, string editionId)
        {
            return featureManager.RevokeAsync(name, FeatureValueProviderNames.Edition, editionId);
        }
    }
}
