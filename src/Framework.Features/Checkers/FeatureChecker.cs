// Copyright (c) Mahmoud Shaheen, 2024. All rights reserved

using Framework.Features.Definitions;
using Framework.Features.Models;
using Framework.Features.ValueProviders;
using Framework.Features.Values;

namespace Framework.Features.Checkers;

public interface IFeatureChecker
{
    Task<string?> GetOrDefaultAsync(string name);

    Task<bool> IsEnabledAsync(string name);
}

public abstract class FeatureCheckerBase : IFeatureChecker
{
    public abstract Task<string?> GetOrDefaultAsync(string name);

    public virtual async Task<bool> IsEnabledAsync(string name)
    {
        var value = await GetOrDefaultAsync(name);

        if (value.IsNullOrEmpty())
        {
            return false;
        }

        try
        {
            return bool.Parse(value);
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException(
                $"The value '{value}' for the feature '{name}' should be a boolean, but was not!",
                ex
            );
        }
    }
}

public sealed class FeatureChecker(
    IFeatureDefinitionManager featureDefinitionManager,
    IFeatureValueProviderManager featureValueProviderManager
) : FeatureCheckerBase
{
    public override async Task<string?> GetOrDefaultAsync(string name)
    {
        var featureDefinition =
            await featureDefinitionManager.GetOrNullAsync(name)
            ?? throw new InvalidOperationException($"Feature {name} is not defined!");

        var providers = featureValueProviderManager.ValueProviders.Reverse();

        if (featureDefinition.AllowedProviders.Count != 0)
        {
            providers = providers.Where(p =>
                featureDefinition.AllowedProviders.Contains(p.Name, StringComparer.Ordinal)
            );
        }

        return await _GetOrDefaultValueFromProvidersAsync(providers, featureDefinition);
    }

    private static async Task<string?> _GetOrDefaultValueFromProvidersAsync(
        IEnumerable<IFeatureValueReadProvider> providers,
        FeatureDefinition feature
    )
    {
        foreach (var provider in providers)
        {
            var value = await provider.GetOrDefaultAsync(feature);

            if (value is not null)
            {
                return value;
            }
        }

        return null;
    }
}
