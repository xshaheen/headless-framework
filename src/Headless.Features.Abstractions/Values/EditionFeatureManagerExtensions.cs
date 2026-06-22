// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Features.Models;

namespace Headless.Features.Values;

/// <summary>Extension members on <see cref="IFeatureManager"/> scoped to the edition provider.</summary>
[PublicAPI]
public static class EditionFeatureManagerExtensions
{
    extension(IFeatureManager featureManager)
    {
        /// <summary>Gets the edition-scoped value of the feature with the given <paramref name="name"/>.</summary>
        /// <param name="name">The feature name.</param>
        /// <param name="editionId">The edition identifier used as the provider key.</param>
        /// <param name="fallback">When <see langword="true"/>, falls back to other providers if the edition provider has no value.</param>
        /// <returns>A <see cref="FeatureValue"/> from the <c>Edition</c> provider.</returns>
        public Task<FeatureValue> GetForEditionAsync(string name, string editionId, bool fallback = true)
        {
            return featureManager.GetAsync(name, FeatureValueProviderNames.Edition, editionId, fallback);
        }

        /// <summary>Gets all feature values scoped to the given edition.</summary>
        /// <param name="editionId">The edition identifier used as the provider key.</param>
        /// <param name="fallback">When <see langword="true"/>, falls back to other providers when the edition provider has no value.</param>
        /// <returns>A list of <see cref="FeatureValue"/> instances for the given edition.</returns>
        public Task<List<FeatureValue>> GetAllForEditionAsync(string editionId, bool fallback = true)
        {
            return featureManager.GetAllAsync(FeatureValueProviderNames.Edition, editionId, fallback);
        }

        /// <summary>Deletes all feature values stored for the given edition.</summary>
        /// <param name="editionId">The edition identifier whose feature values should be deleted.</param>
        /// <param name="cancellationToken">The abort token.</param>
        public Task DeleteForEditionAsync(string editionId, CancellationToken cancellationToken = default)
        {
            return featureManager.DeleteAsync(FeatureValueProviderNames.Edition, editionId, cancellationToken);
        }

        /// <summary>Sets the value of a feature for the given edition.</summary>
        /// <param name="name">The feature name.</param>
        /// <param name="value">The value to store, or <see langword="null"/> to clear it.</param>
        /// <param name="editionId">The edition identifier used as the provider key.</param>
        /// <param name="forceToSet">When <see langword="false"/> and <paramref name="value"/> matches the fallback value, the write is skipped.</param>
        public Task SetForEditionAsync(string name, string? value, string editionId, bool forceToSet = false)
        {
            return featureManager.SetAsync(name, value, FeatureValueProviderNames.Edition, editionId, forceToSet);
        }

        /// <summary>Grants a feature to an edition by setting its value to <see langword="true"/>.</summary>
        /// <param name="name">The feature name.</param>
        /// <param name="editionId">The edition identifier to grant the feature to.</param>
        /// <exception cref="ArgumentNullException"><paramref name="name"/> is <see langword="null"/>.</exception>
        /// <exception cref="Headless.Exceptions.ConflictException">The feature is not defined or the Edition provider is read-only.</exception>
        public Task GrantToEditionAsync(string name, string editionId)
        {
            return featureManager.GrantAsync(name, FeatureValueProviderNames.Edition, editionId);
        }

        /// <summary>Revokes a feature from an edition by setting its value to <see langword="false"/>.</summary>
        /// <param name="name">The feature name.</param>
        /// <param name="editionId">The edition identifier to revoke the feature from.</param>
        /// <exception cref="ArgumentNullException"><paramref name="name"/> is <see langword="null"/>.</exception>
        /// <exception cref="Headless.Exceptions.ConflictException">The feature is not defined or the Edition provider is read-only.</exception>
        public Task RevokeFromEditionAsync(string name, string editionId)
        {
            return featureManager.RevokeAsync(name, FeatureValueProviderNames.Edition, editionId);
        }
    }
}
