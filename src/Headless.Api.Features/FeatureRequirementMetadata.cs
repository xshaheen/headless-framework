// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Features;

namespace Headless.Api.Features;

/// <summary>Enforces the <see cref="RequiresFeatureAttribute"/> requirements carried in an endpoint's metadata.</summary>
internal static class FeatureRequirementMetadata
{
    /// <summary>
    /// Throws when a <see cref="RequiresFeatureAttribute"/> in <paramref name="metadata"/> is not satisfied. A
    /// <see cref="DisableFeatureCheckAttribute"/> anywhere in the metadata skips every requirement, so an action can opt
    /// out of a gate declared on its controller or route group.
    /// </summary>
    /// <exception cref="Headless.ConflictException">A required feature is not enabled.</exception>
    public static async Task EnsureEnabledAsync(
        IFeatureManager featureManager,
        IReadOnlyList<object> metadata,
        CancellationToken cancellationToken
    )
    {
        List<RequiresFeatureAttribute>? requirements = null;

        foreach (var item in metadata)
        {
            switch (item)
            {
                case DisableFeatureCheckAttribute:
                    return;
                case RequiresFeatureAttribute requirement:
                    (requirements ??= []).Add(requirement);
                    break;
            }
        }

        if (requirements is null)
        {
            return;
        }

        foreach (var requirement in requirements)
        {
            await featureManager
                .EnsureEnabledAsync(requirement.IsAnd, requirement.Features, cancellationToken)
                .ConfigureAwait(false);
        }
    }
}
