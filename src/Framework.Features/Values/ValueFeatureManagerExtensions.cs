// Copyright (c) Mahmoud Shaheen, 2024. All rights reserved

using Framework.Features.FeatureManagement;
using Framework.Features.ValueProviders;

namespace Framework.Features.Values;

public static class DefaultValueFeatureManagerExtensions
{
    public static Task<string> GetOrNullDefaultAsync(
        this IFeatureManager featureManager,
        string name,
        bool fallback = true
    )
    {
        return featureManager.GetOrNullAsync(name, DefaultValueFeatureValueProvider.ProviderName, null, fallback);
    }

    public static Task<List<FeatureNameValue>> GetAllDefaultAsync(
        this IFeatureManager featureManager,
        bool fallback = true
    )
    {
        return featureManager.GetAllAsync(DefaultValueFeatureValueProvider.ProviderName, null, fallback);
    }

    public static Task<FeatureNameValueWithGrantedProvider> GetOrNullWithProviderAsync(
        this IFeatureManager featureManager,
        string name,
        bool fallback = true
    )
    {
        return featureManager.GetOrNullWithProviderAsync(
            name,
            DefaultValueFeatureValueProvider.ProviderName,
            null,
            fallback
        );
    }

    public static Task<List<FeatureNameValueWithGrantedProvider>> GetAllWithProviderAsync(
        this IFeatureManager featureManager,
        bool fallback = true
    )
    {
        return featureManager.GetAllWithProviderAsync(DefaultValueFeatureValueProvider.ProviderName, null, fallback);
    }
}

public static class EditionFeatureManagerExtensions
{
    public static Task<string> GetOrNullForEditionAsync(
        this IFeatureManager featureManager,
        string name,
        Guid editionId,
        bool fallback = true
    )
    {
        return featureManager.GetOrNullAsync(
            name,
            EditionFeatureValueProvider.ProviderName,
            editionId.ToString(),
            fallback
        );
    }

    public static Task<List<FeatureNameValue>> GetAllForEditionAsync(
        this IFeatureManager featureManager,
        Guid editionId,
        bool fallback = true
    )
    {
        return featureManager.GetAllAsync(EditionFeatureValueProvider.ProviderName, editionId.ToString(), fallback);
    }

    public static Task<FeatureNameValueWithGrantedProvider> GetOrNullWithProviderForEditionAsync(
        this IFeatureManager featureManager,
        string name,
        Guid editionId,
        bool fallback = true
    )
    {
        return featureManager.GetOrNullWithProviderAsync(
            name,
            EditionFeatureValueProvider.ProviderName,
            editionId.ToString(),
            fallback
        );
    }

    public static Task<List<FeatureNameValueWithGrantedProvider>> GetAllWithProviderForEditionAsync(
        this IFeatureManager featureManager,
        Guid editionId,
        bool fallback = true
    )
    {
        return featureManager.GetAllWithProviderAsync(
            EditionFeatureValueProvider.ProviderName,
            editionId.ToString(),
            fallback
        );
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
    public static Task<string> GetOrNullForTenantAsync(
        this IFeatureManager featureManager,
        string name,
        Guid tenantId,
        bool fallback = true
    )
    {
        return featureManager.GetOrNullAsync(
            name,
            TenantFeatureValueProvider.ProviderName,
            tenantId.ToString(),
            fallback
        );
    }

    public static Task<List<FeatureNameValue>> GetAllForTenantAsync(
        this IFeatureManager featureManager,
        Guid tenantId,
        bool fallback = true
    )
    {
        return featureManager.GetAllAsync(TenantFeatureValueProvider.ProviderName, tenantId.ToString(), fallback);
    }

    public static Task<FeatureNameValueWithGrantedProvider> GetOrNullWithProviderForTenantAsync(
        this IFeatureManager featureManager,
        string name,
        Guid tenantId,
        bool fallback = true
    )
    {
        return featureManager.GetOrNullWithProviderAsync(
            name,
            TenantFeatureValueProvider.ProviderName,
            tenantId.ToString(),
            fallback
        );
    }

    public static Task<List<FeatureNameValueWithGrantedProvider>> GetAllWithProviderForTenantAsync(
        this IFeatureManager featureManager,
        Guid tenantId,
        bool fallback = true
    )
    {
        return featureManager.GetAllWithProviderAsync(
            TenantFeatureValueProvider.ProviderName,
            tenantId.ToString(),
            fallback
        );
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
