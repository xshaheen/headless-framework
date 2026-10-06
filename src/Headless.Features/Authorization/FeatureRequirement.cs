// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;
using Microsoft.AspNetCore.Authorization;

namespace Headless.Features;

/// <summary>
/// ASP.NET Core authorization requirement that demands one or more features to be enabled. When
/// <see cref="RequiresAll"/> is <see langword="true"/> every listed feature must be enabled; when
/// <see langword="false"/> at least one must be.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="RequiresFeatureAttribute"/> adds this requirement to an endpoint's policy; add it to a named policy
/// (<c>policy.RequireFeatures(...)</c> or <c>AddRequirements(new FeatureRequirement(...))</c>) to gate a policy on
/// features directly. The handler that
/// <c>AddHeadlessFeatures</c> registers evaluates it through <see cref="IFeatureManager"/>, for the ambient tenant.
/// </para>
/// <para>
/// The requirement is about the tenant's or edition's state, not about the caller: it does not need an authenticated
/// user. A disabled feature fails it like any other authorization requirement: 403 for an authenticated caller, 401
/// for an anonymous one. The handler fails with the localized "feature currently unavailable" text as its
/// <see cref="AuthorizationFailureReason"/>, which <c>Headless.Api</c> shows as the 403 problem's <c>detail</c>.
/// </para>
/// </remarks>
[PublicAPI]
public sealed class FeatureRequirement : IAuthorizationRequirement
{
    /// <summary>Initializes the requirement.</summary>
    /// <param name="featureNames">The feature names to evaluate, at least one.</param>
    /// <param name="requiresAll">
    /// <see langword="true"/> to require every feature; <see langword="false"/> to require at least one.
    /// </param>
    /// <exception cref="ArgumentNullException"><paramref name="featureNames"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException"><paramref name="featureNames"/> is empty.</exception>
    public FeatureRequirement(string[] featureNames, bool requiresAll)
    {
        Argument.IsNotNullOrEmpty(featureNames);

        FeatureNames = featureNames;
        RequiresAll = requiresAll;
    }

    /// <summary>The feature names to evaluate.</summary>
    public string[] FeatureNames { get; }

    /// <summary>
    /// When <see langword="true"/>, all listed features must be enabled (AND).
    /// When <see langword="false"/>, any single enabled feature satisfies the requirement (OR).
    /// </summary>
    public bool RequiresAll { get; }

    public override string ToString()
    {
        return $"FeatureRequirement: {string.Join(RequiresAll ? " and " : " or ", FeatureNames)}";
    }
}

/// <summary>
/// Marker requirement added by <see cref="DisableFeatureCheckAttribute"/>. The feature handler satisfies it, and
/// every <see cref="FeatureRequirement"/> in the same policy, without evaluating any feature.
/// </summary>
/// <remarks>
/// A marker requirement rather than an endpoint-metadata lookup, so the handler stays free of the HTTP types and the
/// opt-out also holds wherever the combined policy is evaluated.
/// </remarks>
internal sealed class FeatureCheckDisabledRequirement : IAuthorizationRequirement
{
    public static FeatureCheckDisabledRequirement Instance { get; } = new();

    private FeatureCheckDisabledRequirement() { }

    public override string ToString()
    {
        return "FeatureCheckDisabledRequirement";
    }
}
