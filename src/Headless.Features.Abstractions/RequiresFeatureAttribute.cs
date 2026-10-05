// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Features;

/// <summary>
/// Declares that the decorated class or method is available only when the specified feature(s) are enabled.
/// </summary>
/// <remarks>
/// When placed on a class, the check applies to all public methods unless a method overrides it with
/// <see cref="DisableFeatureCheckAttribute"/>. The <see cref="IsAnd"/> property controls whether all features
/// must be enabled (<see langword="true"/>) or any one is sufficient (<see langword="false"/>).
/// The attribute gates nothing by itself: <c>Headless.Api.Features</c> enforces it on HTTP requests, and
/// <c>IMethodInvocationFeatureCheckerService</c> in <c>Headless.Features</c> evaluates it at other call sites.
/// </remarks>
/// <param name="features">The feature names to check.</param>
[PublicAPI]
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method)]
public sealed class RequiresFeatureAttribute(params string[]? features) : Attribute
{
    /// <summary>The feature names to check.</summary>
    public string[] Features { get; } = features ?? [];

    /// <summary>
    /// When <see langword="true"/>, all features in <see cref="Features"/> must be enabled.
    /// When <see langword="false"/>, at least one feature must be enabled.
    /// Default: <see langword="false"/>.
    /// </summary>
    public bool IsAnd { get; set; }
}
