// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;

#pragma warning disable IDE0130 // ReSharper disable once CheckNamespace
namespace Headless.Api.Surfaces;

/// <summary>Immutable surface defaults. Endpoint metadata can override tenancy and anonymous access.</summary>
[PublicAPI]
public sealed class ApiSurfaceDescriptor(
    string surfaceName,
    string? routePrefix,
    string? authorizationPolicy,
    ApiSurfaceTenancyMode tenancyMode,
    ApiSurfaceOpenApiDescriptor openApi
) : IApiSurfaceMetadata
{
    public string SurfaceName { get; } = Argument.IsNotNullOrWhiteSpace(surfaceName);
    public string? RoutePrefix { get; } = routePrefix;
    public string? AuthorizationPolicy { get; } = authorizationPolicy;
    public ApiSurfaceTenancyMode TenancyMode { get; } = tenancyMode;
    public ApiSurfaceOpenApiDescriptor OpenApi { get; } = Argument.IsNotNull(openApi);
}

/// <summary>Immutable document identity for a surface; API version groups remain independent.</summary>
[PublicAPI]
public sealed class ApiSurfaceOpenApiDescriptor(string documentName, string title)
{
    public string DocumentName { get; } = Argument.IsNotNullOrWhiteSpace(documentName);
    public string Title { get; } = Argument.IsNotNullOrWhiteSpace(title);
}
