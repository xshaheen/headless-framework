// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;
using Headless.Exceptions;
using Headless.Features.Resources;

namespace Headless.Features.Values;

/// <summary>General-purpose extension members on <see cref="IFeatureManager"/>.</summary>
[PublicAPI]
public static class FeatureManagerExtensions
{
    extension(IFeatureManager featureManager)
    {
        /// <summary>Returns <see langword="true"/> when the feature with <paramref name="name"/> is enabled.</summary>
        /// <param name="name">The feature name.</param>
        /// <param name="cancellationToken">The abort token.</param>
        /// <returns><see langword="true"/> if the stored value parses to <see langword="true"/>; <see langword="false"/> when the value is absent or parses to <see langword="false"/>.</returns>
        /// <exception cref="InvalidOperationException">The stored value for the feature is not a valid boolean string.</exception>
        public async Task<bool> IsEnabledAsync(string name, CancellationToken cancellationToken = default)
        {
            var featureValue = await featureManager
                .GetAsync(name, cancellationToken: cancellationToken)
                .ConfigureAwait(false);

            if (string.IsNullOrEmpty(featureValue?.Value))
            {
                return false;
            }

            try
            {
                return bool.Parse(featureValue.Value);
            }
            catch (Exception e)
            {
                throw new InvalidOperationException(
                    $"The value '{featureValue.Value}' for the feature '{name}' should be a boolean, but was not!",
                    e
                );
            }
        }

        /// <summary>Checks whether the given features satisfy the <paramref name="requiresAll"/> policy.</summary>
        /// <param name="requiresAll">
        /// When <see langword="true"/>, all features in <paramref name="featureNames"/> must be enabled.
        /// When <see langword="false"/>, at least one must be enabled.
        /// </param>
        /// <param name="featureNames">The feature names to evaluate. An empty or <see langword="null"/> array returns <see langword="true"/>.</param>
        /// <param name="cancellationToken">The abort token.</param>
        /// <returns><see langword="true"/> when the policy is satisfied; otherwise <see langword="false"/>.</returns>
        /// <exception cref="InvalidOperationException">A stored feature value is not a valid boolean string.</exception>
        public async Task<bool> IsEnabledAsync(
            bool requiresAll,
            string[] featureNames,
            CancellationToken cancellationToken = default
        )
        {
            if (featureNames.IsNullOrEmpty())
            {
                return true;
            }

            if (requiresAll)
            {
                foreach (var featureName in featureNames)
                {
                    if (!await featureManager.IsEnabledAsync(featureName, cancellationToken).ConfigureAwait(false))
                    {
                        return false;
                    }
                }

                return true;
            }

            foreach (var featureName in featureNames)
            {
                if (await featureManager.IsEnabledAsync(featureName, cancellationToken).ConfigureAwait(false))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>Gets and converts a feature value to <typeparamref name="T"/>.</summary>
        /// <typeparam name="T">The target type to convert the string feature value to.</typeparam>
        /// <param name="name">The feature name.</param>
        /// <param name="providerName">The provider to query. When <see langword="null"/>, uses the first provider with a value.</param>
        /// <param name="providerKey">Provider-specific key (e.g., tenant ID). When <see langword="null"/>, uses the provider's default logic.</param>
        /// <param name="fallback">When <see langword="true"/>, falls back to other providers when the specified provider has no value.</param>
        /// <param name="cancellationToken">The abort token.</param>
        /// <returns>The feature value converted to <typeparamref name="T"/>, or the default value of <typeparamref name="T"/> if no value exists.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="name"/> is <see langword="null"/>.</exception>
        public async Task<T?> GetAsync<T>(
            string name,
            string? providerName = null,
            string? providerKey = null,
            bool fallback = true,
            CancellationToken cancellationToken = default
        )
        {
            Argument.IsNotNull(featureManager);
            Argument.IsNotNull(name);

            var value = await featureManager
                .GetAsync(name, providerName, providerKey, fallback, cancellationToken: cancellationToken)
                .ConfigureAwait(false);

            return value.To<T>();
        }

        /// <summary>Throws <see cref="Headless.Exceptions.ConflictException"/> when the feature with <paramref name="featureName"/> is not enabled.</summary>
        /// <param name="featureName">The feature that must be enabled.</param>
        /// <param name="cancellationToken">The abort token.</param>
        /// <exception cref="Headless.Exceptions.ConflictException">The feature is currently unavailable or disabled.</exception>
        /// <exception cref="InvalidOperationException">The stored feature value is not a valid boolean string.</exception>
        public async Task EnsureEnabledAsync(string featureName, CancellationToken cancellationToken = default)
        {
            if (await featureManager.IsEnabledAsync(featureName, cancellationToken).ConfigureAwait(false))
            {
                return;
            }

            var error = MessageDescriber
                .FeatureCurrentlyUnavailable()
                .WithParam("Type", "Single")
                .WithParam("FeatureNames", new[] { featureName });

            throw new ConflictException(error);
        }

        /// <summary>
        /// Throws <see cref="Headless.Exceptions.ConflictException"/> when the given features do not satisfy
        /// the <paramref name="requiresAll"/> policy.
        /// </summary>
        /// <param name="requiresAll">
        /// When <see langword="true"/>, all features must be enabled.
        /// When <see langword="false"/>, at least one must be enabled.
        /// </param>
        /// <param name="featureNames">The feature names to evaluate. An empty or <see langword="null"/> array is treated as satisfied (no exception).</param>
        /// <param name="cancellationToken">The abort token.</param>
        /// <exception cref="Headless.Exceptions.ConflictException">The required feature(s) are currently unavailable or disabled.</exception>
        /// <exception cref="InvalidOperationException">A stored feature value is not a valid boolean string.</exception>
        public async Task EnsureEnabledAsync(
            bool requiresAll,
            string[] featureNames,
            CancellationToken cancellationToken = default
        )
        {
            if (featureNames.IsNullOrEmpty())
            {
                return;
            }

            if (requiresAll)
            {
                foreach (var featureName in featureNames)
                {
                    if (!await featureManager.IsEnabledAsync(featureName, cancellationToken).ConfigureAwait(false))
                    {
                        var error = MessageDescriber
                            .FeatureCurrentlyUnavailable()
                            .WithParam("Type", "And")
                            .WithParam("FeatureNames", featureNames);

                        throw new ConflictException(error);
                    }
                }
            }
            else
            {
                foreach (var featureName in featureNames)
                {
                    if (await featureManager.IsEnabledAsync(featureName, cancellationToken).ConfigureAwait(false))
                    {
                        return;
                    }
                }

                var error = MessageDescriber
                    .FeatureCurrentlyUnavailable()
                    .WithParam("Type", "Or")
                    .WithParam("FeatureNames", featureNames);

                throw new ConflictException(error);
            }
        }

        /// <summary>Grants a feature by setting its value to <c>"true"</c> for the given provider and key, forcing the write even when the value matches the fallback.</summary>
        /// <param name="name">The feature name.</param>
        /// <param name="providerName">The provider to write the value to.</param>
        /// <param name="providerKey">The provider-specific key (e.g., tenant ID or edition ID).</param>
        /// <exception cref="ArgumentNullException"><paramref name="name"/> or <paramref name="providerName"/> is <see langword="null"/>.</exception>
        /// <exception cref="Headless.Exceptions.ConflictException">The feature is not defined, the provider is not registered, or the provider is read-only.</exception>
        public Task GrantAsync(string name, string providerName, string providerKey)
        {
            return featureManager.SetAsync(name, "true", providerName, providerKey, forceToSet: true);
        }

        /// <summary>Revokes a feature by setting its value to <c>"false"</c> for the given provider and key, forcing the write even when the value matches the fallback.</summary>
        /// <param name="name">The feature name.</param>
        /// <param name="providerName">The provider to write the value to.</param>
        /// <param name="providerKey">The provider-specific key (e.g., tenant ID or edition ID).</param>
        /// <exception cref="ArgumentNullException"><paramref name="name"/> or <paramref name="providerName"/> is <see langword="null"/>.</exception>
        /// <exception cref="Headless.Exceptions.ConflictException">The feature is not defined, the provider is not registered, or the provider is read-only.</exception>
        public Task RevokeAsync(string name, string providerName, string providerKey)
        {
            return featureManager.SetAsync(name, "false", providerName, providerKey, forceToSet: true);
        }
    }
}
