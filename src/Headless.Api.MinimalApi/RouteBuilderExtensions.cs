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
    /// before the endpoint handler is invoked.
    /// </summary>
    /// <remarks>
    /// Headless.OpenApi documents the 400 the filter writes for a missing body and the 422 it writes for validation
    /// errors on every operation, so the filter adds no response metadata.
    /// </remarks>
    /// <typeparam name="TArgument">The request type whose validators should be applied.</typeparam>
    /// <param name="builder">The route handler builder to add the filter to.</param>
    /// <returns><paramref name="builder"/> for chaining.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="builder"/> is <see langword="null"/>.</exception>
    public static RouteHandlerBuilder Validate<TArgument>(this RouteHandlerBuilder builder)
    {
        Argument.IsNotNull(builder);

        return builder.AddEndpointFilter<MinimalApiValidatorFilter<TArgument>>();
    }

    /// <summary>
    /// Validates, on every endpoint the builder configures, each handler argument whose parameter type has a
    /// registered <see cref="global::FluentValidation.IValidator{T}"/>. Apply it to a route group to validate every
    /// endpoint in the group.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The arguments are chosen once per endpoint when the endpoint is built: an endpoint with no validated
    /// parameter gets no filter. All failures of all validated arguments return together as one 422 response; the
    /// handler does not run. Headless.OpenApi documents that 422 on every operation, so the filter adds no response
    /// metadata.
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

        // The filter must be in place before the request delegate is built, so it is a regular convention.
        builder.Add(static endpoint => ArgumentValidationEndpointFilter.Apply(endpoint));

        return builder;
    }
}
