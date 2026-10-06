// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Microsoft.AspNetCore.Authorization;

namespace Headless.Features;

/// <summary>
/// Marks a method to skip every feature requirement that would otherwise apply to it, such as a
/// <see cref="RequiresFeatureAttribute"/> on the containing class, a route group, or a route convention.
/// Apply this to action methods that should be accessible regardless of whether a feature is enabled.
/// </summary>
/// <remarks>
/// On HTTP endpoints the attribute is authorization data, like <see cref="RequiresFeatureAttribute"/>: it adds a
/// marker requirement to the endpoint's policy, and the feature handler satisfies every
/// <see cref="FeatureRequirement"/> in a policy that carries the marker. It skips feature requirements only; the
/// endpoint's other policies still apply.
/// </remarks>
[PublicAPI]
[AttributeUsage(AttributeTargets.Method)]
public sealed class DisableFeatureCheckAttribute : Attribute, IAuthorizationRequirementData
{
    /// <summary>Returns the marker requirement that switches off the endpoint's feature requirements.</summary>
    /// <returns>The authorization requirements to add to the endpoint's policy.</returns>
    public IEnumerable<IAuthorizationRequirement> GetRequirements()
    {
        return [FeatureCheckDisabledRequirement.Instance];
    }
}
