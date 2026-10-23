// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Microsoft.AspNetCore.Builder;

namespace Headless.Api;

/// <summary>
/// Extension members on <see cref="IEndpointConventionBuilder"/> that turn the standard 404, 409, and 422 problem
/// responses, and the statuses the application mapped through <see cref="HeadlessProblemDetailsOptions"/>, on or off
/// in the API document for Minimal API endpoints, route groups, and controller conventions.
/// </summary>
/// <remarks>
/// The innermost setting wins: an endpoint over its route group, a group over the groups around it. On
/// <c>MapControllers()</c> the convention is the outermost setting, so a <see cref="StandardProblemResponsesAttribute"/>
/// on a controller or action beats it. Any of these beats the surface's setting, including a marker on a group around
/// <c>MapApiSurface(...)</c>, and the surface's setting beats the API-wide one.
/// </remarks>
[PublicAPI]
public static class StandardProblemResponsesEndpointConventionBuilderExtensions
{
    extension<TBuilder>(TBuilder builder)
        where TBuilder : IEndpointConventionBuilder
    {
        /// <summary>
        /// Documents the standard problem responses on the endpoint or every endpoint of the group, even where the
        /// surface or the API-wide setting turns them off. A setting on an inner endpoint or group, or a controller or
        /// action attribute under <c>MapControllers()</c>, wins.
        /// </summary>
        /// <returns>The same endpoint convention builder.</returns>
        public TBuilder WithStandardProblemResponses()
        {
            return builder.WithMetadata(new StandardProblemResponsesAttribute(enabled: true));
        }

        /// <summary>
        /// Leaves the standard problem responses out of the document for the endpoint or every endpoint of the group,
        /// such as a health check. A setting on an inner endpoint or group, or a controller or action attribute under
        /// <c>MapControllers()</c>, wins. The endpoint still returns these statuses when a handler or service raises
        /// them.
        /// </summary>
        /// <returns>The same endpoint convention builder.</returns>
        public TBuilder WithoutStandardProblemResponses()
        {
            return builder.WithMetadata(new StandardProblemResponsesAttribute(enabled: false));
        }
    }
}
