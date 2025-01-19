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

        var value = await featureManager.GetOrDefaultAsync(
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
}

[PublicAPI]
public static class DefaultValueFeatureManagerExtensions
{
    public static Task<FeatureValue?> GetDefaultAsync(
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

    public static Task<List<FeatureValue>> GetAllDefaultAsync(
        this IFeatureManager featureManager,
        bool fallback = true,
        CancellationToken cancellationToken = default
    )
    {
        return featureManager.GetAllAsync(
            DefaultValueFeatureValueProvider.ProviderName,
            providerKey: null,
            fallback,
            cancellationToken
        );
    }
}

[PublicAPI]
public static class EditionFeatureManagerExtensions
{
    public static Task<FeatureValue?> GetForEditionAsync(
        this IFeatureManager featureManager,
        string editionId,
        string name,
        bool fallback = true
    )
    {
        return featureManager.GetOrDefaultAsync(name, EditionFeatureValueProvider.ProviderName, editionId, fallback);
    }

    public static Task<List<FeatureValue>> GetAllForEditionAsync(
        this IFeatureManager featureManager,
        string editionId,
        bool fallback = true
    )
    {
        return featureManager.GetAllAsync(EditionFeatureValueProvider.ProviderName, editionId, fallback);
    }

    public static Task SetForEditionAsync(
        this IFeatureManager featureManager,
        string editionId,
        string name,
        string? value,
        bool forceToSet = false
    )
    {
        return featureManager.SetAsync(name, value, EditionFeatureValueProvider.ProviderName, editionId, forceToSet);
    }

    public static Task DeleteForEditionAsync(
        this IFeatureManager featureManager,
        string editionId,
        CancellationToken cancellationToken = default
    )
    {
        return featureManager.DeleteAsync(EditionFeatureValueProvider.ProviderName, editionId, cancellationToken);
    }
}

[PublicAPI]
public static class TenantFeatureManagerExtensions
{
    public static Task<FeatureValue?> GetForTenantAsync(
        this IFeatureManager featureManager,
        string tenantId,
        string name,
        bool fallback = true
    )
    {
        return featureManager.GetOrDefaultAsync(name, TenantFeatureValueProvider.ProviderName, tenantId, fallback);
    }

    public static Task<List<FeatureValue>> GetAllForTenantAsync(
        this IFeatureManager featureManager,
        string tenantId,
        bool fallback = true
    )
    {
        return featureManager.GetAllAsync(TenantFeatureValueProvider.ProviderName, tenantId, fallback);
    }

    public static Task SetForTenantAsync(
        this IFeatureManager featureManager,
        string tenantId,
        string name,
        string? value,
        bool forceToSet = false
    )
    {
        return featureManager.SetAsync(name, value, TenantFeatureValueProvider.ProviderName, tenantId, forceToSet);
    }

    public static Task DeleteForTenantAsync(
        this IFeatureManager featureManager,
        string tenantId,
        CancellationToken cancellationToken = default
    )
    {
        return featureManager.DeleteAsync(TenantFeatureValueProvider.ProviderName, tenantId, cancellationToken);
    }
}
