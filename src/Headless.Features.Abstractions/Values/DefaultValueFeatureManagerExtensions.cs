// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Features.Models;

namespace Headless.Features.Values;

/// <summary>Extension methods on <see cref="IFeatureManager"/> for the default-value provider.</summary>
[PublicAPI]
public static class DefaultValueFeatureManagerExtensions
{
    /// <summary>Gets the default-provider value of the feature with the given <paramref name="name"/>.</summary>
    /// <param name="featureManager">The feature manager.</param>
    /// <param name="name">The feature name.</param>
    /// <param name="fallback">When <see langword="true"/>, falls back to other providers if the default-value provider has no value.</param>
    /// <returns>A <see cref="FeatureValue"/> from the <c>DefaultValue</c> provider.</returns>
    public static Task<FeatureValue> GetDefaultAsync(
        this IFeatureManager featureManager,
        string name,
        bool fallback = true
    )
    {
        return featureManager.GetAsync(name, FeatureValueProviderNames.DefaultValue, providerKey: null, fallback);
    }

    /// <summary>Gets all feature values from the default-value provider.</summary>
    /// <param name="featureManager">The feature manager.</param>
    /// <param name="fallback">When <see langword="true"/>, falls back to other providers if the default-value provider has no value for a feature.</param>
    /// <param name="cancellationToken">The abort token.</param>
    /// <returns>A list of <see cref="FeatureValue"/> instances from the <c>DefaultValue</c> provider.</returns>
    public static Task<List<FeatureValue>> GetAllDefaultAsync(
        this IFeatureManager featureManager,
        bool fallback = true,
        CancellationToken cancellationToken = default
    )
    {
        return featureManager.GetAllAsync(
            FeatureValueProviderNames.DefaultValue,
            providerKey: null,
            fallback,
            cancellationToken
        );
    }
}
