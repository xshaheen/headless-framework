// Copyright (c) Mahmoud Shaheen, 2024. All rights reserved

using Framework.Features.Models;
using Framework.Features.ValueProviders;

namespace Framework.Features.Values;

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
