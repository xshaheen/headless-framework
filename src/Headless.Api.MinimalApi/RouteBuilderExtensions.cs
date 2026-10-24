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

    /// <summary>
    /// Binds route, query, header, and form values on every endpoint the builder configures with the rules an MVC
    /// action follows under <c>ConfigureHeadlessMvc()</c>: an enum is read by the names the JSON body accepts for it,
    /// and a value that cannot be bound answers 400 <c>g:invalid_parameter_value</c> naming it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// ASP.NET Core parses an enum outside the body with a case-sensitive <c>Enum.TryParse</c> that also accepts
    /// numbers, so <c>?status=active</c> fails while the OpenAPI document advertises <c>active</c>, and <c>?status=1</c>
    /// binds. Here an enum value is read as the body reads the same text (a name the naming policy derives in any
    /// casing, a <c>[JsonStringEnumMemberName]</c> alias as written, comma-separated <c>[Flags]</c> names, no numbers), and any other simple value is checked with the parse ASP.NET Core applies, so the 400 names the value
    /// instead of answering the generic bad-request problem. A body that cannot be read keeps that problem, as in MVC.
    /// </para>
    /// <para>
    /// <see cref="HeadlessApiSurfaceEndpointRouteBuilderExtensions.MapApiSurface"/> applies it to every endpoint of a
    /// surface; call it on any other group or endpoint. The values to check are chosen once per endpoint when the
    /// endpoint is built, an endpoint with none runs unchanged, and applying it twice has no further effect.
    /// </para>
    /// </remarks>
    /// <typeparam name="TBuilder">The endpoint convention builder type, such as a route group or a route handler.</typeparam>
    /// <param name="builder">The builder to configure.</param>
    /// <returns><paramref name="builder"/> for chaining.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="builder"/> is <see langword="null"/>.</exception>
    public static TBuilder BindRequestValues<TBuilder>(this TBuilder builder)
        where TBuilder : IEndpointConventionBuilder
    {
        Argument.IsNotNull(builder);

        // It wraps the request delegate, which exists only once the endpoint is built, so it is a final convention.
        builder.Finally(static endpoint => RequestValueBinder.Apply(endpoint));

        return builder;
    }
}
