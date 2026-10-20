// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;

namespace Headless.Api;

/// <summary>Configures one surface before registration freezes its definition.</summary>
[PublicAPI]
public sealed class ApiSurfaceBuilder(string surfaceName)
{
    public string SurfaceName { get; } = Argument.IsNotNullOrWhiteSpace(surfaceName);
    public string? RoutePrefix { get; set; }

    /// <summary>An additional named authorization policy. Native AllowAnonymous metadata still applies.</summary>
    public string? DefaultAuthorizationPolicy { get; set; }

    /// <summary>Default tenancy metadata. RequireTenant needs a TenantRequirement in the endpoint's authorization policy.</summary>
    public ApiSurfaceTenancyMode DefaultTenancyMode { get; set; }

    public ApiSurfaceOpenApiOptions OpenApi { get; } = new(surfaceName);

    internal ApiSurfaceDescriptor Build() =>
        new(
            SurfaceName,
            string.IsNullOrWhiteSpace(RoutePrefix) ? null : RoutePrefix.Trim('/'),
            DefaultAuthorizationPolicy,
            DefaultTenancyMode,
            new ApiSurfaceOpenApiDescriptor(OpenApi.DocumentName, OpenApi.Title, OpenApi.StandardProblemResponses)
        );
}

/// <summary>Configures document identity independently of ApiExplorer version groups.</summary>
[PublicAPI]
public sealed class ApiSurfaceOpenApiOptions(string surfaceName)
{
    public string DocumentName { get; set; } = surfaceName.ToLowerInvariant();
    public string Title { get; set; } = $"{surfaceName} API";

    /// <summary>
    /// Whether the surface's endpoints document the standard 404, 409, and 422 problem responses. Default is
    /// <see langword="null"/>, which inherits the API-wide setting.
    /// </summary>
    /// <remarks>
    /// Set it to <see langword="false"/> for a surface whose endpoints should not advertise these statuses, such as an
    /// internal operations surface. An endpoint, route group, controller, or action marked with
    /// <see cref="StandardProblemResponsesAttribute"/> overrides it.
    /// </remarks>
    public bool? StandardProblemResponses { get; set; }
}
