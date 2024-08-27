using Framework.Api.Core.Resources;
using Framework.Arguments;
using Framework.BuildingBlocks;

namespace Framework.Features.Checkers;

public static class FeatureCheckerExtensions
{
    public static async Task<T> GetAsync<T>(this IFeatureChecker featureChecker, string name, T defaultValue = default)
        where T : struct
    {
        Argument.IsNotNull(featureChecker);
        Argument.IsNotNull(name);

        var value = await featureChecker.GetOrDefaultAsync(name);

        return value?.To<T>() ?? defaultValue;
    }

    public static async Task<bool> IsEnabledAsync(
        this IFeatureChecker featureChecker,
        bool requiresAll,
        params string[] featureNames
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
                if (!await featureChecker.IsEnabledAsync(featureName))
                {
                    return false;
                }
            }

            return true;
        }

        foreach (var featureName in featureNames)
        {
            if (await featureChecker.IsEnabledAsync(featureName))
            {
                return true;
            }
        }

        return false;
    }

    public static async Task CheckEnabledAsync(this IFeatureChecker featureChecker, string featureName)
    {
        if (!await featureChecker.IsEnabledAsync(featureName))
        {
            var error = SharedMessageDescriber
                .General.FeatureCurrentlyUnavailable()
                .WithParam("Type", "Single")
                .WithParam("FeatureNames", new[] { featureName });

            throw new ConflictException(error);
        }
    }

    public static async Task CheckEnabledAsync(
        this IFeatureChecker featureChecker,
        bool requiresAll,
        params string[] featureNames
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
                if (!await featureChecker.IsEnabledAsync(featureName))
                {
                    var error = SharedMessageDescriber
                        .General.FeatureCurrentlyUnavailable()
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
                if (await featureChecker.IsEnabledAsync(featureName))
                {
                    return;
                }
            }

            var error = SharedMessageDescriber
                .General.FeatureCurrentlyUnavailable()
                .WithParam("Type", "Or")
                .WithParam("FeatureNames", featureNames);

            throw new ConflictException(error);
        }
    }
}
