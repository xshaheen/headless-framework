// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Microsoft.AspNetCore.Authorization;

namespace Headless.Features;

/// <summary>
/// Declares that the decorated class or method is available only when the specified feature(s) are enabled.
/// </summary>
/// <remarks>
/// <para>
/// On HTTP endpoints the attribute is authorization data: ASP.NET Core's authorization middleware adds a
/// <see cref="FeatureRequirement"/> to the endpoint's policy, which the handler that <c>AddHeadlessFeatures</c>
/// registers evaluates. It applies to MVC controllers and actions and to Minimal API handlers and route groups
/// (<c>.WithMetadata(new RequiresFeatureAttribute(...))</c>). The requirement combines with the endpoint's other
/// policies, and with the fallback policy when the endpoint declares no <c>[Authorize]</c>. It never requires an
/// authenticated user, so anonymous callers are gated too; an endpoint marked <c>[AllowAnonymous]</c> skips
/// authorization entirely, feature requirements included.
/// </para>
/// <para>
/// When placed on a class, the check applies to all public methods unless a method overrides it with
/// <see cref="DisableFeatureCheckAttribute"/>. The <see cref="IsAnd"/> property controls whether all features
/// must be enabled (<see langword="true"/>) or any one is sufficient (<see langword="false"/>). Outside HTTP,
/// <c>IMethodInvocationFeatureCheckerService</c> in <c>Headless.Features</c> evaluates the same attribute.
/// </para>
/// </remarks>
/// <param name="features">The feature names to check.</param>
[PublicAPI]
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method)]
public sealed class RequiresFeatureAttribute(params string[]? features) : Attribute, IAuthorizationRequirementData
{
    /// <summary>The feature names to check.</summary>
    public string[] Features { get; } = features ?? [];

    /// <summary>
    /// When <see langword="true"/>, all features in <see cref="Features"/> must be enabled.
    /// When <see langword="false"/>, at least one feature must be enabled.
    /// Default: <see langword="false"/>.
    /// </summary>
    public bool IsAnd { get; set; }

    /// <summary>
    /// Returns the <see cref="FeatureRequirement"/> this attribute declares, or nothing when <see cref="Features"/> is
    /// empty, matching <c>IFeatureManager.EnsureEnabledAsync</c>, which treats an empty list as satisfied.
    /// </summary>
    /// <returns>The authorization requirements to add to the endpoint's policy.</returns>
    public IEnumerable<IAuthorizationRequirement> GetRequirements()
    {
        // Built per call rather than cached: IsAnd is settable after construction, and the authorization middleware
        // caches the combined policy per endpoint, so this runs once per endpoint.
        return Features.Length == 0 ? [] : [new FeatureRequirement(Features, IsAnd)];
    }
}
