// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Framework.Api.Resources;
using Framework.Checks;
using Framework.Exceptions;
using Framework.Features.Models;
using Framework.Features.ValueProviders;

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
        var featureValue = await featureManager.GetOrDefaultAsync(name, cancellationToken: cancellationToken);

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

    public static async Task<T> GetAsync<T>(
        this IFeatureManager featureManager,
        string name,
        T defaultValue = default,
        CancellationToken cancellationToken = default
    )
        where T : struct
    {
        Argument.IsNotNull(featureManager);
        Argument.IsNotNull(name);

        var value = await featureManager.GetOrDefaultAsync(name, cancellationToken: cancellationToken);

        return value?.To<T>() ?? defaultValue;
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
}

[PublicAPI]
public static class DefaultValueFeatureManagerExtensions
{
    public static Task<FeatureValue?> GetOrDefaultDefaultAsync(
        this IFeatureManager featureManager,
        string name,
        bool fallback = true
    )
    {
        return featureManager.GetOrDefaultAsync(
            name,
            DefaultValueFeatureValueProvider.ProviderName,
            providerKey: null,
            fallback
        );
    }

    public static Task<List<FeatureValue>> GetAllDefaultAsync(this IFeatureManager featureManager, bool fallback = true)
    {
        return featureManager.GetAllAsync(DefaultValueFeatureValueProvider.ProviderName, providerKey: null, fallback);
    }
}

[PublicAPI]
public static class EditionFeatureManagerExtensions
{
    public static Task<FeatureValue?> GetOrDefaultForEditionAsync(
        this IFeatureManager featureManager,
        string name,
        Guid editionId,
        bool fallback = true
    )
    {
        return featureManager.GetOrDefaultAsync(
            name,
            EditionFeatureValueProvider.ProviderName,
            editionId.ToString(),
            fallback
        );
    }

    public static Task<List<FeatureValue>> GetAllForEditionAsync(
        this IFeatureManager featureManager,
        Guid editionId,
        bool fallback = true
    )
    {
        return featureManager.GetAllAsync(EditionFeatureValueProvider.ProviderName, editionId.ToString(), fallback);
    }

    public static Task SetForEditionAsync(
        this IFeatureManager featureManager,
        Guid editionId,
        string name,
        string? value,
        bool forceToSet = false
    )
    {
        return featureManager.SetAsync(
            name,
            value,
            EditionFeatureValueProvider.ProviderName,
            editionId.ToString(),
            forceToSet
        );
    }
}

[PublicAPI]
public static class TenantFeatureManagerExtensions
{
    public static Task<FeatureValue?> GetOrDefaultForTenantAsync(
        this IFeatureManager featureManager,
        string name,
        Guid tenantId,
        bool fallback = true
    )
    {
        return featureManager.GetOrDefaultAsync(
            name,
            TenantFeatureValueProvider.ProviderName,
            tenantId.ToString(),
            fallback
        );
    }

    public static Task<List<FeatureValue>> GetAllForTenantAsync(
        this IFeatureManager featureManager,
        Guid tenantId,
        bool fallback = true
    )
    {
        return featureManager.GetAllAsync(TenantFeatureValueProvider.ProviderName, tenantId.ToString(), fallback);
    }

    public static Task SetForTenantAsync(
        this IFeatureManager featureManager,
        Guid tenantId,
        string name,
        string? value,
        bool forceToSet = false
    )
    {
        return featureManager.SetAsync(
            name,
            value,
            TenantFeatureValueProvider.ProviderName,
            tenantId.ToString(),
            forceToSet
        );
    }
}
