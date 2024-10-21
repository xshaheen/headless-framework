// Copyright (c) Mahmoud Shaheen, 2024. All rights reserved

using Framework.Features.Providers;

namespace Framework.Features.FeatureManagement;

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
