// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Framework.Api.Resources;
using Framework.Checks;
using Framework.Exceptions;

namespace Framework.Features.Values;

[PublicAPI]
public static class FeatureManagerExtensions
{
    public static async Task<bool> IsEnabledAsync(
        this IFeatureManager featureManager,
        string name,
        CancellationToken cancellationToken = default
    )
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

    public static async Task<bool> IsEnabledAsync(
        this IFeatureManager featureManager,
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

    public static async Task<T?> GetAsync<T>(
        this IFeatureManager featureManager,
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

    public static async Task EnsureEnabledAsync(
        this IFeatureManager featureManager,
        string featureName,
        CancellationToken cancellationToken = default
    )
    {
        if (await featureManager.IsEnabledAsync(featureName, cancellationToken))
        {
            return;
        }

        var error = GeneralMessageDescriber
            .FeatureCurrentlyUnavailable()
            .WithParam("Type", "Single")
            .WithParam("FeatureNames", new[] { featureName });

        throw new ConflictException(error);
    }

    public static async Task EnsureEnabledAsync(
        this IFeatureManager featureManager,
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
                    var error = GeneralMessageDescriber
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

            var error = GeneralMessageDescriber
                .FeatureCurrentlyUnavailable()
                .WithParam("Type", "Or")
                .WithParam("FeatureNames", featureNames);

            throw new ConflictException(error);
        }
    }

    public static Task GrantAsync(
        this IFeatureManager featureManager,
        string name,
        string providerName,
        string providerKey
    )
    {
        return featureManager.SetAsync(name, "true", providerName, providerKey, forceToSet: true);
    }

    public static Task RevokeAsync(
        this IFeatureManager featureManager,
        string name,
        string providerName,
        string providerKey
    )
    {
        return featureManager.SetAsync(name, "false", providerName, providerKey, forceToSet: true);
    }
}
