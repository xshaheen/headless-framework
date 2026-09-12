// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;

namespace Headless.Api.Surfaces;

/// <summary>Configures one surface before AddHeadlessApiSurfaces freezes its definition.</summary>
[PublicAPI]
public sealed class ApiSurfaceBuilder(string surfaceName)
{
    public string SurfaceName { get; } = Argument.IsNotNullOrWhiteSpace(surfaceName);
    public string? RoutePrefix { get; set; }

    /// <summary>An additional named authorization policy. Native AllowAnonymous metadata still applies.</summary>
    public string? AuthorizationPolicy { get; set; }

    /// <summary>Default tenancy metadata. RequireTenant needs a TenantRequirement in the endpoint's authorization policy.</summary>
    public ApiSurfaceTenancyMode TenancyMode { get; set; }

    public ApiSurfaceOpenApiOptions OpenApi { get; } = new(surfaceName);

    internal ApiSurfaceDescriptor Build() =>
        new(
            SurfaceName,
            string.IsNullOrWhiteSpace(RoutePrefix) ? null : RoutePrefix.Trim('/'),
            AuthorizationPolicy,
            TenancyMode,
            new ApiSurfaceOpenApiDescriptor(OpenApi.DocumentName, OpenApi.Title)
        );
}

/// <summary>Configures document identity independently of ApiExplorer version groups.</summary>
[PublicAPI]
public sealed class ApiSurfaceOpenApiOptions(string surfaceName)
{
    public string DocumentName { get; set; } = surfaceName.ToLowerInvariant();
    public string Title { get; set; } = $"{surfaceName} API";
}
