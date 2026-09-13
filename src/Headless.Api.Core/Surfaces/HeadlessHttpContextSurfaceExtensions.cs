// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Api.Surfaces;
using Headless.Checks;
using Microsoft.Extensions.DependencyInjection;

#pragma warning disable IDE0130 // ReSharper disable once CheckNamespace
namespace Microsoft.AspNetCore.Http;

[PublicAPI]
public static class HeadlessHttpContextSurfaceExtensions
{
    /// <summary>
    /// Returns the selected endpoint's configured surface defaults, not its effective authorization.
    /// Call after routing. Returns null when no endpoint or surface metadata is selected.
    /// Resolves the current endpoint on every call so re-execution cannot retain stale defaults.
    /// </summary>
    /// <exception cref="ArgumentNullException">The context is null.</exception>
    /// <exception cref="InvalidOperationException">The endpoint names a surface whose services or configuration are missing.</exception>
    public static ApiSurfaceDescriptor? GetApiSurface(this HttpContext context)
    {
        Argument.IsNotNull(context);

        var metadata = context.GetEndpoint()?.Metadata.GetMetadata<IApiSurfaceMetadata>();
        return metadata is null
            ? null
            : context.RequestServices.GetRequiredService<ApiSurfaceRegistry>().GetRequiredSurface(metadata.SurfaceName);
    }
}
