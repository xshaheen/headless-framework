// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;

namespace Headless.Api.Surfaces;

/// <summary>Immutable surface defaults. Endpoint metadata can override tenancy and anonymous access.</summary>
[PublicAPI]
public sealed class ApiSurfaceDescriptor(
    string surfaceName,
    string? routePrefix,
    string? defaultAuthorizationPolicy,
    ApiSurfaceTenancyMode defaultTenancyMode,
    ApiSurfaceOpenApiDescriptor openApi
) : IApiSurfaceMetadata
{
    public string SurfaceName { get; } = Argument.IsNotNullOrWhiteSpace(surfaceName);
    public string? RoutePrefix { get; } = routePrefix;
    public string? DefaultAuthorizationPolicy { get; } = defaultAuthorizationPolicy;
    public ApiSurfaceTenancyMode DefaultTenancyMode { get; } = defaultTenancyMode;
    public ApiSurfaceOpenApiDescriptor OpenApi { get; } = Argument.IsNotNull(openApi);
}
