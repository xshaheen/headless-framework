// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Diagnostics;
using Headless.Checks;
using Microsoft.AspNetCore.Http;

namespace Headless.Api.Surfaces;

/// <summary>Attaches surface defaults and trace context after routing, before authentication and authorization.</summary>
public sealed class ApiSurfaceMiddleware(RequestDelegate next, ApiSurfaceRegistry registry)
{
    private readonly RequestDelegate _next = Argument.IsNotNull(next);
    private readonly ApiSurfaceRegistry _registry = Argument.IsNotNull(registry);

    public Task InvokeAsync(HttpContext context)
    {
        var endpoint = context.GetEndpoint();
        var metadata = endpoint?.Metadata.GetMetadata<IApiSurfaceMetadata>();
        var feature = metadata is null ? null : _registry.GetRequiredFeature(metadata.SurfaceName);
        // Re-execution can select another endpoint; never retain the previous request classification.
        context.Features.Set(feature);
        Activity.Current?.SetTag(
            "api.surface",
            feature?.SurfaceName ?? (endpoint is null ? "unknown" : "unclassified")
        );
        return _next(context);
    }
}
