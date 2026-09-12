// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Api.Surfaces;
using Headless.Checks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

#pragma warning disable IDE0130 // ReSharper disable once CheckNamespace
namespace Headless.Api;

[PublicAPI]
public static class HeadlessApiSurfaceEndpointRouteBuilderExtensions
{
    /// <summary>Maps a surface's route prefix and authorization defaults. Endpoint tenancy choices take precedence.</summary>
    /// <exception cref="InvalidOperationException">The surface is unregistered or an endpoint belongs to conflicting surfaces.</exception>
    public static RouteGroupBuilder MapApiSurface(
        this IEndpointRouteBuilder endpoints,
        string surfaceName,
        Action<RouteGroupBuilder>? configure = null
    )
    {
        Argument.IsNotNull(endpoints);
        Argument.IsNotNullOrWhiteSpace(surfaceName);
        var descriptor = endpoints
            .ServiceProvider.GetRequiredService<ApiSurfaceRegistry>()
            .GetRequiredSurface(surfaceName);
        var group = endpoints.MapGroup(descriptor.RoutePrefix ?? string.Empty);
        group.WithMetadata(descriptor);
        if (!string.IsNullOrWhiteSpace(descriptor.AuthorizationPolicy))
        {
            group.RequireAuthorization(descriptor.AuthorizationPolicy);
        }

        ((IEndpointConventionBuilder)group).Finally(builder =>
        {
            if (
                builder
                    .Metadata.OfType<IApiSurfaceMetadata>()
                    .Any(x => !string.Equals(x.SurfaceName, descriptor.SurfaceName, StringComparison.OrdinalIgnoreCase))
            )
            {
                throw new InvalidOperationException(
                    $"Endpoint '{builder.DisplayName}' belongs to conflicting API surfaces."
                );
            }

            var tenancy = ApiSurfaceEndpointDefaults.GetTenancyMetadata(descriptor.TenancyMode, builder.Metadata);
            if (tenancy is not null)
            {
                builder.Metadata.Insert(0, tenancy);
            }
        });
        configure?.Invoke(group);
        return group;
    }
}
