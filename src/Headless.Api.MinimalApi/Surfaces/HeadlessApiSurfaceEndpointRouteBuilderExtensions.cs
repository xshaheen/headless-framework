// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Metadata;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace Headless.Api;

[PublicAPI]
public static class HeadlessApiSurfaceEndpointRouteBuilderExtensions
{
    /// <summary>
    /// Maps a surface's route prefix, authorization, tenancy, and OpenAPI tag defaults. Endpoint tenancy choices and
    /// tags take precedence.
    /// </summary>
    /// <remarks>
    /// An endpoint with no tag of its own is tagged with the surface's OpenAPI title. Every endpoint refuses an enum sent
    /// as a number, as <see cref="RouteBuilderExtensions.RejectEnumNumbers{TBuilder}"/> describes. Chain
    /// <see cref="RouteBuilderExtensions.ValidateArguments{TBuilder}"/> on the returned group to validate arguments
    /// across the surface.
    /// </remarks>
    /// <param name="endpoints">The route builder.</param>
    /// <param name="surfaceName">The registered surface name.</param>
    /// <param name="configure">Maps the surface's endpoints on the group.</param>
    /// <returns>The surface's route group.</returns>
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
        if (!string.IsNullOrWhiteSpace(descriptor.DefaultAuthorizationPolicy))
        {
            group.RequireAuthorization(descriptor.DefaultAuthorizationPolicy);
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

            var tenancy = ApiSurfaceEndpointDefaults.GetTenancyMetadata(
                descriptor.DefaultTenancyMode,
                builder.Metadata
            );
            if (tenancy is not null)
            {
                builder.Metadata.Insert(0, tenancy);
            }

            // A default, not an override: any tag the endpoint or a nested group set wins, and an untagged endpoint
            // groups under its surface instead of the application name OpenAPI falls back to.
            if (!builder.Metadata.OfType<ITagsMetadata>().Any())
            {
                builder.Metadata.Add(new TagsAttribute(descriptor.OpenApi.Title));
            }
        });
        group.RejectEnumNumbers();
        configure?.Invoke(group);
        return group;
    }
}
