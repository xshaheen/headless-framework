// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;

namespace Headless.Api;

/// <summary>
/// Extension members on <see cref="IEndpointConventionBuilder"/> that apply the same-origin guard to Minimal API
/// endpoints, route groups, and <c>MapControllers()</c>.
/// </summary>
[PublicAPI]
public static class SameOriginEndpointConventionBuilderExtensions
{
    extension<TBuilder>(TBuilder builder)
        where TBuilder : IEndpointConventionBuilder
    {
        /// <summary>
        /// Refuses a request unless it comes from the API's own origin or a trusted one, as
        /// <see cref="HeadlessSameOriginOptions"/> configures, answering 403 with <c>g:cross_site_request</c>. Apply it
        /// to every endpoint that authenticates through a cookie, such as refresh and logout.
        /// </summary>
        /// <remarks>
        /// The guard runs as an endpoint filter, so it covers Minimal API handlers and controller actions mapped by
        /// <c>MapControllers()</c>. Endpoints without a filter pipeline, such as SignalR hubs, ignore it. The endpoint
        /// also carries <see cref="RequireSameOriginAttribute"/> as metadata.
        /// </remarks>
        /// <returns>The same endpoint convention builder.</returns>
        public TBuilder RequireSameOrigin()
        {
            return builder
                .WithMetadata(new RequireSameOriginAttribute())
                .AddEndpointFilter(new SameOriginEndpointFilter());
        }
    }
}
