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

[PublicAPI]
public static class DefaultValueFeatureManagerExtensions
{
    public static Task<FeatureValue> GetDefaultAsync(
        this IFeatureManager featureManager,
        string name,
        bool fallback = true
    )
    {
        return featureManager.GetAsync(
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
    public static Task<FeatureValue> GetForEditionAsync(
        this IFeatureManager featureManager,
        string name,
        string editionId,
        bool fallback = true
    )
    {
        return featureManager.GetAsync(name, EditionFeatureValueProvider.ProviderName, editionId, fallback);
    }

    public static Task<List<FeatureValue>> GetAllForEditionAsync(
        this IFeatureManager featureManager,
        string editionId,
        bool fallback = true
    )
    {
        return featureManager.GetAllAsync(EditionFeatureValueProvider.ProviderName, editionId, fallback);
    }

    public static Task DeleteForEditionAsync(
        this IFeatureManager featureManager,
        string editionId,
        CancellationToken cancellationToken = default
    )
    {
        return featureManager.DeleteAsync(EditionFeatureValueProvider.ProviderName, editionId, cancellationToken);
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
        return featureManager.SetAsync(name, value, EditionFeatureValueProvider.ProviderName, editionId, forceToSet);
    }

    /// <summary>Grant a feature to an edition.</summary>
    public static Task GrantToEditionAsync(this IFeatureManager featureManager, string name, string editionId)
    {
        return featureManager.GrantAsync(name, EditionFeatureValueProvider.ProviderName, editionId);
    }

    /// <summary>Revoke a feature from an edition.</summary>
    public static Task RevokeFromEditionAsync(this IFeatureManager featureManager, string name, string editionId)
    {
        return featureManager.RevokeAsync(name, EditionFeatureValueProvider.ProviderName, editionId);
    }
}

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
        return featureManager.GetAsync(name, TenantFeatureValueProvider.ProviderName, tenantId, fallback);
    }

    public static Task<List<FeatureValue>> GetAllForTenantAsync(
        this IFeatureManager featureManager,
        string tenantId,
        bool fallback = true
    )
    {
        return featureManager.GetAllAsync(TenantFeatureValueProvider.ProviderName, tenantId, fallback);
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

    /// <summary>Grant a feature to a tenant.</summary>
    public static Task GrantToTenantAsync(this IFeatureManager featureManager, string name, string tenantId)
    {
        return featureManager.GrantAsync(name, TenantFeatureValueProvider.ProviderName, tenantId);
    }

    /// <summary>Revoke a feature from a tenant.</summary>
    public static Task RevokeFromTenantAsync(this IFeatureManager featureManager, string name, string tenantId)
    {
        return featureManager.RevokeAsync(name, TenantFeatureValueProvider.ProviderName, tenantId);
    }
}
