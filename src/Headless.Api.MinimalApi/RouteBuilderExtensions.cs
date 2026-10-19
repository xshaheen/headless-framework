// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;

namespace Headless.Api;

[PublicAPI]
public static class RouteBuilderExtensions
{
    /// <summary>Requires exactly one strong <c>If-Match</c> entity tag before invoking the endpoint.</summary>
    /// <param name="builder">The route handler builder to configure.</param>
    /// <returns><paramref name="builder"/> for chaining.</returns>
    public static RouteHandlerBuilder RequireIfMatch(this RouteHandlerBuilder builder)
    {
        return builder.WithMetadata(new RequireIfMatchAttribute()).AddEndpointFilter(new IfMatchEndpointFilter());
    }

    /// <summary>
    /// Emits an <c>ETag</c> response field when a successful endpoint result exposes
    /// <see cref="IHasEntityTag"/>.
    /// </summary>
    /// <param name="builder">The route handler builder to configure.</param>
    /// <returns><paramref name="builder"/> for chaining.</returns>
    public static RouteHandlerBuilder WithEntityTag(this RouteHandlerBuilder builder)
    {
        return builder.AddEndpointFilter(new EntityTagResponseEndpointFilter());
    }

    /// <summary>
    /// Adds <see cref="MinimalApiValidatorFilter{TRequest}"/> to the endpoint, which runs all registered
    /// <see cref="global::FluentValidation.IValidator{T}"/> implementations for <typeparamref name="TArgument"/>
    /// before the endpoint handler is invoked, and documents the filter's 400 (missing body) and 422 (validation errors)
    /// problem responses for OpenAPI.
    /// </summary>
    /// <typeparam name="TArgument">The request type whose validators should be applied.</typeparam>
    /// <param name="builder">The route handler builder to add the filter to.</param>
    /// <returns><paramref name="builder"/> for chaining.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="builder"/> is <see langword="null"/>.</exception>
    public static RouteHandlerBuilder Validate<TArgument>(this RouteHandlerBuilder builder)
    {
        Argument.IsNotNull(builder);
        builder.AddEndpointFilter<MinimalApiValidatorFilter<TArgument>>();
        ((IEndpointConventionBuilder)builder).Finally(static endpoint =>
        {
            ApiResultProblemResponse.AddProblemIfMissing(endpoint, StatusCodes.Status400BadRequest);
            ApiResultProblemResponse.AddProblemIfMissing(endpoint, StatusCodes.Status422UnprocessableEntity);
        });

        return builder;
    }

    /// <summary>
    /// Validates, on every endpoint the builder configures, each handler argument whose parameter type has a
    /// registered <see cref="global::FluentValidation.IValidator{T}"/>, and documents the endpoint's 422 problem
    /// response for OpenAPI. Apply it to a route group to validate every endpoint in the group.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The arguments are chosen once per endpoint when the endpoint is built: an endpoint with no validated
    /// parameter gets no filter and no 422 metadata. All failures of all validated arguments return together as
    /// one 422 response; the handler does not run.
    /// </para>
    /// <para>
    /// Do not also apply <see cref="Validate{TArgument}"/> for the same argument, or its validators run twice.
    /// </para>
    /// </remarks>
    /// <typeparam name="TBuilder">The endpoint convention builder type, such as a route group or a route handler.</typeparam>
    /// <param name="builder">The builder to configure.</param>
    /// <returns><paramref name="builder"/> for chaining.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="builder"/> is <see langword="null"/>.</exception>
    public static TBuilder ValidateArguments<TBuilder>(this TBuilder builder)
        where TBuilder : IEndpointConventionBuilder
    {
        Argument.IsNotNull(builder);

        // The filter must be in place before the request delegate is built, so it is a regular convention; the 422
        // metadata waits for Finally so it sees what the handler's own result type already documents.
        builder.Add(static endpoint => ArgumentValidationEndpointFilter.Apply(endpoint));
        builder.Finally(static endpoint =>
        {
            if (endpoint.Metadata.Contains(ValidatedEndpointMetadata.Instance))
            {
                ApiResultProblemResponse.AddProblemIfMissing(endpoint, StatusCodes.Status422UnprocessableEntity);
            }
        });

        return builder;
    }

    /// <summary>
    /// Documents the standard problem-details responses (400, 401, 403, 404, 409, 422, and 429, as
    /// <c>application/problem+json</c>) on every endpoint the builder configures, skipping any status the endpoint
    /// already documents. Apply it to a route group to document every endpoint in the group.
    /// </summary>
    /// <typeparam name="TBuilder">The endpoint convention builder type, such as a route group or a route handler.</typeparam>
    /// <param name="builder">The builder to configure.</param>
    /// <returns><paramref name="builder"/> for chaining.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="builder"/> is <see langword="null"/>.</exception>
    public static TBuilder ProducesStandardProblems<TBuilder>(this TBuilder builder)
        where TBuilder : IEndpointConventionBuilder
    {
        Argument.IsNotNull(builder);

        builder.Finally(static endpoint =>
        {
            foreach (var statusCode in ApiResultProblemResponse.StandardProblemStatusCodes)
            {
                ApiResultProblemResponse.AddProblemIfMissing(endpoint, statusCode);
            }
        });

        return builder;
    }
}
