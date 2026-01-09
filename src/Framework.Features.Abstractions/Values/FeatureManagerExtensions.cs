// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Framework.Checks;
using Framework.Exceptions;
using Framework.Features.Resources;
using Framework.Primitives;

namespace Framework.Features.Values;

[PublicAPI]
public static class FeatureManagerExtensions
{
    extension(IFeatureManager featureManager)
    {
        public async Task<bool> IsEnabledAsync(string name, CancellationToken cancellationToken = default)
        {
            var featureValue = await featureManager.GetAsync(name, cancellationToken: cancellationToken);

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
                    if (!await featureManager.IsEnabledAsync(featureName, cancellationToken))
                    {
                        return false;
                    }
                }

                return true;
            }

            foreach (var featureName in featureNames)
            {
                if (await featureManager.IsEnabledAsync(featureName, cancellationToken))
                {
                    return true;
                }
            }

            return false;
        }

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

            var value = await featureManager.GetAsync(
                name,
                providerName,
                providerKey,
                fallback,
                cancellationToken: cancellationToken
            );

            return value.To<T>();
        }

        public async Task EnsureEnabledAsync(string featureName, CancellationToken cancellationToken = default)
        {
            if (await featureManager.IsEnabledAsync(featureName, cancellationToken))
            {
                return;
            }

            var error = MessageDescriber
                .FeatureCurrentlyUnavailable()
                .WithParam("Type", "Single")
                .WithParam("FeatureNames", new[] { featureName });

            throw new ConflictException(error);
        }

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
                    if (!await featureManager.IsEnabledAsync(featureName, cancellationToken))
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
                    if (await featureManager.IsEnabledAsync(featureName, cancellationToken))
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

        public Task GrantAsync(string name, string providerName, string providerKey)
        {
            return featureManager.SetAsync(name, "true", providerName, providerKey, forceToSet: true);
        }

        public Task RevokeAsync(string name, string providerName, string providerKey)
        {
            return featureManager.SetAsync(name, "false", providerName, providerKey, forceToSet: true);
        }
    }
}
