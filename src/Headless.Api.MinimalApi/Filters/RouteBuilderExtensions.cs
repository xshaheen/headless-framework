// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;

namespace Headless.Api;

[PublicAPI]
public static class RouteBuilderExtensions
{
    /// <summary>
    /// Adds <see cref="MinimalApiValidatorFilter{TRequest}"/> to the endpoint, which runs all registered
    /// <see cref="FluentValidation.IValidator{T}"/> implementations for <typeparamref name="TArgument"/>
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
