// Copyright (c) Mahmoud Shaheen, 2024. All rights reserved

using Framework.Api.Resources;
using Framework.Kernel.Checks;
using Framework.Kernel.Primitives;

namespace Framework.Features.Checkers;

[PublicAPI]
public static class FeatureCheckerExtensions
{
    public static async Task<T> GetAsync<T>(
        this IFeatureChecker featureChecker,
        string name,
        T defaultValue = default,
        CancellationToken cancellationToken = default
    )
        where T : struct
    {
        Argument.IsNotNull(featureChecker);
        Argument.IsNotNull(name);

        var value = await featureChecker.GetOrDefaultAsync(name, cancellationToken);

        return value?.To<T>() ?? defaultValue;
    }

    public static async Task<bool> IsEnabledAsync(
        this IFeatureChecker featureChecker,
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
                if (!await featureChecker.IsEnabledAsync(featureName, cancellationToken))
                {
                    return false;
                }
            }

            return true;
        }

        foreach (var featureName in featureNames)
        {
            if (await featureChecker.IsEnabledAsync(featureName, cancellationToken))
            {
                return true;
            }
        }

        return false;
    }

    public static async Task CheckEnabledAsync(
        this IFeatureChecker featureChecker,
        string featureName,
        CancellationToken cancellationToken = default
    )
    {
        if (!await featureChecker.IsEnabledAsync(featureName, cancellationToken))
        {
            var error = GeneralMessageDescriber
                .FeatureCurrentlyUnavailable()
                .WithParam("Type", "Single")
                .WithParam("FeatureNames", new[] { featureName });

            throw new ConflictException(error);
        }
    }

    public static async Task CheckEnabledAsync(
        this IFeatureChecker featureChecker,
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
                if (!await featureChecker.IsEnabledAsync(featureName, cancellationToken))
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
                if (await featureChecker.IsEnabledAsync(featureName, cancellationToken))
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
