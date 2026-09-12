// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Collections.Frozen;
using System.Diagnostics;
using Headless.Checks;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;

namespace Headless.Api.Surfaces;

/// <summary>
/// Observes the routed endpoint and sets the ambient API surface telemetry tag and feature.
/// Runs after routing and before authentication/authorization, ensuring telemetry tags
/// are present even if the request is rejected with 401 or 403.
/// </summary>
public sealed class ApiSurfaceMiddleware
{
    private const string _ApiSurfaceTag = "api.surface";
    private const string _InfrastructureSurface = "infrastructure";
    private const string _UnknownSurface = "unknown";

    private readonly RequestDelegate _next;
    private readonly FrozenDictionary<string, ApiSurfaceFeature> _features;

    public ApiSurfaceMiddleware(RequestDelegate next, IOptions<ApiSurfaceOptions> options)
    {
        _next = Argument.IsNotNull(next);
        var surfaceOptions = Argument.IsNotNull(options).Value;
        _features = surfaceOptions.BuildFeatureLookup();
    }

    public async Task InvokeAsync(HttpContext context)
    {
        var endpoint = context.GetEndpoint();
        if (endpoint is null)
        {
            Activity.Current?.SetTag(_ApiSurfaceTag, _UnknownSurface);
            await _next(context);
            return;
        }

        var surfaceMetadata = endpoint.Metadata.GetMetadata<IApiSurfaceMetadata>();
        if (surfaceMetadata is null)
        {
            Activity.Current?.SetTag(_ApiSurfaceTag, _InfrastructureSurface);
            await _next(context);
            return;
        }

        if (_features.TryGetValue(surfaceMetadata.SurfaceName, out var feature))
        {
            context.Features.Set(feature);
            Activity.Current?.SetTag(_ApiSurfaceTag, feature.SurfaceName);
        }
        else
        {
            var adHocFeature = new ApiSurfaceFeature(surfaceMetadata.SurfaceName);
            context.Features.Set(adHocFeature);
            Activity.Current?.SetTag(_ApiSurfaceTag, surfaceMetadata.SurfaceName);
        }

        await _next(context);
    }
}
