// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Api.MultiTenancy;
using Headless.Api.Surfaces;
using Headless.Checks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

#pragma warning disable IDE0130 // ReSharper disable once CheckNamespace
namespace Headless.Api;

public static class SurfaceEndpointRouteBuilderExtensions
{
    private sealed class SurfaceMetadata(string surfaceName) : IApiSurfaceMetadata
    {
        public string SurfaceName { get; } = surfaceName;
    }

    /// <summary>
    /// Maps a route group belonging to a named API surface partition, automatically applying
    /// configured route prefix, authorization policy, tenancy posture, and OpenAPI grouping.
    /// </summary>
    /// <param name="endpoints">The endpoint route builder.</param>
    /// <param name="surfaceName">The unique name of the surface partition.</param>
    /// <param name="configure">Delegate to map routes inside this surface group.</param>
    /// <returns>The created <see cref="RouteGroupBuilder"/>.</returns>
    public static RouteGroupBuilder MapSurfaceGroup(
        this IEndpointRouteBuilder endpoints,
        string surfaceName,
        Action<RouteGroupBuilder> configure
    )
    {
        Argument.IsNotNull(endpoints);
        Argument.IsNotNullOrWhiteSpace(surfaceName);
        Argument.IsNotNull(configure);

        var options = endpoints.ServiceProvider.GetRequiredService<IOptions<ApiSurfaceOptions>>().Value;
        if (!options.TryGetSurface(surfaceName, out var descriptor) || descriptor is null)
        {
            throw new InvalidOperationException(
                $"API surface '{surfaceName}' has not been configured in ApiSurfaceOptions."
            );
        }

        var prefix = string.IsNullOrWhiteSpace(descriptor.RoutePrefix)
            ? string.Empty
            : descriptor.RoutePrefix.Trim('/');
        var group = endpoints.MapGroup(prefix);

        group.WithMetadata(new SurfaceMetadata(descriptor.SurfaceName));

        if (!string.IsNullOrWhiteSpace(descriptor.GroupName))
        {
            group.WithGroupName(descriptor.GroupName);
        }

        if (!string.IsNullOrWhiteSpace(descriptor.RequiredPolicy))
        {
            group.RequireAuthorization(descriptor.RequiredPolicy);
        }

        switch (descriptor.TenancyPosture)
        {
            case SurfaceTenancyPosture.RequireTenant:
                group.WithMetadata(new RequireTenantAttribute());
                break;
            case SurfaceTenancyPosture.AllowMissingTenant:
                group.WithMetadata(new AllowMissingTenantAttribute());
                break;
            case SurfaceTenancyPosture.SkipTenantResolution:
                group.WithMetadata(new SkipTenantResolutionAttribute());
                break;
            case SurfaceTenancyPosture.Unspecified:
            default:
                break;
        }

        configure(group);

        return group;
    }
}
