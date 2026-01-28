using Headless.Features.Models;

namespace Headless.Features.Values;

[PublicAPI]
public static class DefaultValueFeatureManagerExtensions
{
    public static Task<FeatureValue> GetDefaultAsync(
        this IFeatureManager featureManager,
        string name,
        bool fallback = true
    )
    {
        return featureManager.GetAsync(name, FeatureValueProviderNames.DefaultValue, providerKey: null, fallback);
    }

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
