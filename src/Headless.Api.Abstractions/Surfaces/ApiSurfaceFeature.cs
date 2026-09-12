// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;

#pragma warning disable IDE0130 // ReSharper disable once CheckNamespace
namespace Headless.Api.Surfaces;

/// <summary>
/// HttpContext feature containing details about the resolved API surface for the current request.
/// </summary>
public sealed class ApiSurfaceFeature(
    string surfaceName,
    string? routePrefix = null,
    string? requiredPolicy = null,
    SurfaceTenancyPosture tenancyPosture = SurfaceTenancyPosture.Unspecified
)
{
    /// <summary>
    /// Gets the unique name of the API surface.
    /// </summary>
    public string SurfaceName { get; } = Argument.IsNotNullOrWhiteSpace(surfaceName);

    /// <summary>
    /// Gets the optional route prefix associated with this surface (e.g., "api/portal").
    /// </summary>
    public string? RoutePrefix { get; } = routePrefix;

    /// <summary>
    /// Gets the optional authorization policy enforced on this surface.
    /// </summary>
    public string? RequiredPolicy { get; } = requiredPolicy;

    /// <summary>
    /// Gets the tenancy posture configured for this surface.
    /// </summary>
    public SurfaceTenancyPosture TenancyPosture { get; } = tenancyPosture;
}
