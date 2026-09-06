// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Abstractions;
using Headless.Api.Concurrency;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;

#pragma warning disable IDE0130 // ReSharper disable once CheckNamespace
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
    /// <typeparam name="TArgument">The request type whose validators should be applied.</typeparam>
    /// <param name="builder">The route handler builder to add the filter to.</param>
    /// <returns><paramref name="builder"/> for chaining.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="builder"/> is <see langword="null"/>.</exception>
    public static RouteHandlerBuilder Validate<TArgument>(this RouteHandlerBuilder builder)
    {
        return builder.AddEndpointFilter<MinimalApiValidatorFilter<TArgument>>();
    }
}
