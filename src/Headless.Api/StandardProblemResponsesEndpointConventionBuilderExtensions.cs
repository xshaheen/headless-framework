// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Microsoft.AspNetCore.Builder;

namespace Headless.Api;

/// <summary>
/// Extension members on <see cref="IEndpointConventionBuilder"/> that turn the standard 404, 409, and 422 problem
/// responses on or off in the API document for Minimal API endpoints, route groups, and controller conventions.
/// </summary>
[PublicAPI]
public static class StandardProblemResponsesEndpointConventionBuilderExtensions
{
    extension<TBuilder>(TBuilder builder)
        where TBuilder : IEndpointConventionBuilder
    {
        /// <summary>
        /// Documents the standard problem responses on the endpoint or every endpoint of the group, even where the
        /// surface or the API-wide setting turns them off. A setting on an inner endpoint or group wins.
        /// </summary>
        /// <returns>The same endpoint convention builder.</returns>
        public TBuilder WithStandardProblemResponses()
        {
            return builder.WithMetadata(new StandardProblemResponsesAttribute(enabled: true));
        }

        /// <summary>
        /// Leaves the standard problem responses out of the document for the endpoint or every endpoint of the group,
        /// such as a health check. A setting on an inner endpoint or group wins. The endpoint still returns these
        /// statuses when a handler or service raises them.
        /// </summary>
        /// <returns>The same endpoint convention builder.</returns>
        public TBuilder WithoutStandardProblemResponses()
        {
            return builder.WithMetadata(new StandardProblemResponsesAttribute(enabled: false));
        }
    }
}
