// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;

namespace Headless.Api.Surfaces;

/// <summary>
/// Describes configuration for a specific API surface partition.
/// </summary>
public sealed class ApiSurfaceDescriptor(string surfaceName)
{
    /// <summary>
    /// Gets the unique identifier for this surface.
    /// </summary>
    public string SurfaceName { get; } = Argument.IsNotNullOrWhiteSpace(surfaceName);

    /// <summary>
    /// Gets or sets the route prefix for endpoints belonging to this surface (e.g., "api/portal" or "api/console").
    /// When set, leading and trailing slashes are trimmed.
    /// </summary>
    public string? RoutePrefix { get; set; }

    /// <summary>
    /// Gets or sets the default authorization policy required by endpoints on this surface.
    /// </summary>
    public string? RequiredPolicy { get; set; }

    /// <summary>
    /// Gets or sets the OpenAPI / ApiExplorer group name for endpoints in this surface. Defaults to <see cref="SurfaceName"/>.
    /// </summary>
    public string GroupName { get; set; } = surfaceName;

    /// <summary>
    /// Gets or sets the OpenAPI document name used in URLs (e.g. "portal" for /openapi/portal.json). Defaults to lower-case <see cref="SurfaceName"/>.
    /// </summary>
    public string DocumentName { get; set; } = surfaceName.ToLowerInvariant();

    /// <summary>
    /// Gets or sets the human-readable OpenAPI document title. Defaults to "{SurfaceName} API".
    /// </summary>
    public string DocumentTitle { get; set; } = $"{surfaceName} API";

    /// <summary>
    /// Gets or sets the tenancy posture enforced on this surface. Defaults to <see cref="SurfaceTenancyPosture.Unspecified"/>.
    /// </summary>
    public SurfaceTenancyPosture TenancyPosture { get; set; } = SurfaceTenancyPosture.Unspecified;

    /// <summary>
    /// Creates an immutable <see cref="ApiSurfaceFeature"/> snapshot representing this descriptor.
    /// </summary>
    public ApiSurfaceFeature ToFeature()
    {
        var normalizedPrefix = string.IsNullOrWhiteSpace(RoutePrefix) ? null : RoutePrefix.Trim('/');
        return new ApiSurfaceFeature(SurfaceName, normalizedPrefix, RequiredPolicy, TenancyPosture);
    }
}
