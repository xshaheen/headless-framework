// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Diagnostics.CodeAnalysis;
using Headless.Checks;
using Headless.Primitives;
using Mediator;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Routing;

namespace Headless.Api;

/// <summary>
/// Maps a route whose whole handler is "bind the mediator request, send it, return its <see cref="ApiResult"/>".
/// </summary>
/// <remarks>
/// <para>
/// GET and DELETE bind the request with <see cref="AsParametersAttribute"/>, from route values and the query string.
/// POST, PUT, and PATCH bind it from the JSON body; an empty body answers 400 Bad Request without reaching the
/// mediator. A body whose properties also come from the route needs its own handler: bind the parts and call
/// <see cref="HeadlessSenderExtensions"/>.
/// </para>
/// <para>
/// The two-type-parameter overloads return 200 OK with the handler's value; the one-type-parameter overloads return
/// 204 No Content. Every route publishes its success type and the 400/401/403/404/409/422 problem responses to
/// OpenAPI.
/// </para>
/// </remarks>
[PublicAPI]
public static class HeadlessRequestEndpointRouteBuilderExtensions
{
    /// <summary>Maps a GET route that binds <typeparamref name="TRequest"/> from route and query values and returns 200 OK.</summary>
    /// <typeparam name="TRequest">The mediator request.</typeparam>
    /// <typeparam name="TResponse">The success value type.</typeparam>
    /// <param name="endpoints">The route builder.</param>
    /// <param name="pattern">The route pattern.</param>
    /// <returns>The endpoint builder for further conventions.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="endpoints"/> or <paramref name="pattern"/> is <see langword="null"/>.</exception>
    public static RouteHandlerBuilder MapGetRequest<TRequest, TResponse>(
        this IEndpointRouteBuilder endpoints,
        [StringSyntax("Route")] string pattern
    )
        where TRequest : IRequest<ApiResult<TResponse>>
    {
        Argument.IsNotNull(endpoints);
        Argument.IsNotNull(pattern);

        return endpoints.MapGet(pattern, _FromParameters<TRequest, TResponse>);
    }

    /// <summary>Maps a GET route that binds <typeparamref name="TRequest"/> from route and query values and returns 204 No Content.</summary>
    /// <typeparam name="TRequest">The mediator request.</typeparam>
    /// <param name="endpoints">The route builder.</param>
    /// <param name="pattern">The route pattern.</param>
    /// <returns>The endpoint builder for further conventions.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="endpoints"/> or <paramref name="pattern"/> is <see langword="null"/>.</exception>
    public static RouteHandlerBuilder MapGetRequest<TRequest>(
        this IEndpointRouteBuilder endpoints,
        [StringSyntax("Route")] string pattern
    )
        where TRequest : IRequest<ApiResult>
    {
        Argument.IsNotNull(endpoints);
        Argument.IsNotNull(pattern);

        return endpoints.MapGet(pattern, _FromParameters<TRequest>);
    }

    /// <summary>Maps a DELETE route that binds <typeparamref name="TRequest"/> from route and query values and returns 200 OK.</summary>
    /// <typeparam name="TRequest">The mediator request.</typeparam>
    /// <typeparam name="TResponse">The success value type.</typeparam>
    /// <param name="endpoints">The route builder.</param>
    /// <param name="pattern">The route pattern.</param>
    /// <returns>The endpoint builder for further conventions.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="endpoints"/> or <paramref name="pattern"/> is <see langword="null"/>.</exception>
    public static RouteHandlerBuilder MapDeleteRequest<TRequest, TResponse>(
        this IEndpointRouteBuilder endpoints,
        [StringSyntax("Route")] string pattern
    )
        where TRequest : IRequest<ApiResult<TResponse>>
    {
        Argument.IsNotNull(endpoints);
        Argument.IsNotNull(pattern);

        return endpoints.MapDelete(pattern, _FromParameters<TRequest, TResponse>);
    }

    /// <summary>Maps a DELETE route that binds <typeparamref name="TRequest"/> from route and query values and returns 204 No Content.</summary>
    /// <typeparam name="TRequest">The mediator request.</typeparam>
    /// <param name="endpoints">The route builder.</param>
    /// <param name="pattern">The route pattern.</param>
    /// <returns>The endpoint builder for further conventions.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="endpoints"/> or <paramref name="pattern"/> is <see langword="null"/>.</exception>
    public static RouteHandlerBuilder MapDeleteRequest<TRequest>(
        this IEndpointRouteBuilder endpoints,
        [StringSyntax("Route")] string pattern
    )
        where TRequest : IRequest<ApiResult>
    {
        Argument.IsNotNull(endpoints);
        Argument.IsNotNull(pattern);

        return endpoints.MapDelete(pattern, _FromParameters<TRequest>);
    }

    /// <summary>Maps a POST route that binds <typeparamref name="TRequest"/> from the JSON body and returns 200 OK.</summary>
    /// <typeparam name="TRequest">The mediator request.</typeparam>
    /// <typeparam name="TResponse">The success value type.</typeparam>
    /// <param name="endpoints">The route builder.</param>
    /// <param name="pattern">The route pattern.</param>
    /// <returns>The endpoint builder for further conventions.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="endpoints"/> or <paramref name="pattern"/> is <see langword="null"/>.</exception>
    public static RouteHandlerBuilder MapPostRequest<TRequest, TResponse>(
        this IEndpointRouteBuilder endpoints,
        [StringSyntax("Route")] string pattern
    )
        where TRequest : IRequest<ApiResult<TResponse>>
    {
        Argument.IsNotNull(endpoints);
        Argument.IsNotNull(pattern);

        return endpoints.MapPost(pattern, _FromBody<TRequest, TResponse>);
    }

    /// <summary>
    /// Maps a POST route that binds <typeparamref name="TRequest"/> from the JSON body and returns 201 Created with a
    /// <c>Location</c> field.
    /// </summary>
    /// <typeparam name="TRequest">The mediator request.</typeparam>
    /// <typeparam name="TResponse">The created resource type.</typeparam>
    /// <param name="endpoints">The route builder.</param>
    /// <param name="pattern">The route pattern.</param>
    /// <param name="location">Builds the created resource's URI from the success value; called only on success.</param>
    /// <returns>The endpoint builder for further conventions.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="endpoints"/>, <paramref name="pattern"/>, or <paramref name="location"/> is <see langword="null"/>.</exception>
    public static RouteHandlerBuilder MapPostRequest<TRequest, TResponse>(
        this IEndpointRouteBuilder endpoints,
        [StringSyntax("Route")] string pattern,
        Func<TResponse, string> location
    )
        where TRequest : IRequest<ApiResult<TResponse>>
    {
        Argument.IsNotNull(endpoints);
        Argument.IsNotNull(pattern);
        Argument.IsNotNull(location);

        return endpoints.MapPost(
            pattern,
            (
                [FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Allow)] TRequest? request,
                [FromServices] ISender sender,
                CancellationToken cancellationToken
            ) => sender.DispatchCreatedAsync(request, location, cancellationToken)
        );
    }

    /// <summary>Maps a POST route that binds <typeparamref name="TRequest"/> from the JSON body and returns 204 No Content.</summary>
    /// <typeparam name="TRequest">The mediator request.</typeparam>
    /// <param name="endpoints">The route builder.</param>
    /// <param name="pattern">The route pattern.</param>
    /// <returns>The endpoint builder for further conventions.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="endpoints"/> or <paramref name="pattern"/> is <see langword="null"/>.</exception>
    public static RouteHandlerBuilder MapPostRequest<TRequest>(
        this IEndpointRouteBuilder endpoints,
        [StringSyntax("Route")] string pattern
    )
        where TRequest : IRequest<ApiResult>
    {
        Argument.IsNotNull(endpoints);
        Argument.IsNotNull(pattern);

        return endpoints.MapPost(pattern, _FromBody<TRequest>);
    }

    /// <summary>Maps a PUT route that binds <typeparamref name="TRequest"/> from the JSON body and returns 200 OK.</summary>
    /// <typeparam name="TRequest">The mediator request.</typeparam>
    /// <typeparam name="TResponse">The success value type.</typeparam>
    /// <param name="endpoints">The route builder.</param>
    /// <param name="pattern">The route pattern.</param>
    /// <returns>The endpoint builder for further conventions.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="endpoints"/> or <paramref name="pattern"/> is <see langword="null"/>.</exception>
    public static RouteHandlerBuilder MapPutRequest<TRequest, TResponse>(
        this IEndpointRouteBuilder endpoints,
        [StringSyntax("Route")] string pattern
    )
        where TRequest : IRequest<ApiResult<TResponse>>
    {
        Argument.IsNotNull(endpoints);
        Argument.IsNotNull(pattern);

        return endpoints.MapPut(pattern, _FromBody<TRequest, TResponse>);
    }

    /// <summary>Maps a PUT route that binds <typeparamref name="TRequest"/> from the JSON body and returns 204 No Content.</summary>
    /// <typeparam name="TRequest">The mediator request.</typeparam>
    /// <param name="endpoints">The route builder.</param>
    /// <param name="pattern">The route pattern.</param>
    /// <returns>The endpoint builder for further conventions.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="endpoints"/> or <paramref name="pattern"/> is <see langword="null"/>.</exception>
    public static RouteHandlerBuilder MapPutRequest<TRequest>(
        this IEndpointRouteBuilder endpoints,
        [StringSyntax("Route")] string pattern
    )
        where TRequest : IRequest<ApiResult>
    {
        Argument.IsNotNull(endpoints);
        Argument.IsNotNull(pattern);

        return endpoints.MapPut(pattern, _FromBody<TRequest>);
    }

    /// <summary>Maps a PATCH route that binds <typeparamref name="TRequest"/> from the JSON body and returns 200 OK.</summary>
    /// <typeparam name="TRequest">The mediator request.</typeparam>
    /// <typeparam name="TResponse">The success value type.</typeparam>
    /// <param name="endpoints">The route builder.</param>
    /// <param name="pattern">The route pattern.</param>
    /// <returns>The endpoint builder for further conventions.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="endpoints"/> or <paramref name="pattern"/> is <see langword="null"/>.</exception>
    public static RouteHandlerBuilder MapPatchRequest<TRequest, TResponse>(
        this IEndpointRouteBuilder endpoints,
        [StringSyntax("Route")] string pattern
    )
        where TRequest : IRequest<ApiResult<TResponse>>
    {
        Argument.IsNotNull(endpoints);
        Argument.IsNotNull(pattern);

        return endpoints.MapPatch(pattern, _FromBody<TRequest, TResponse>);
    }

    /// <summary>Maps a PATCH route that binds <typeparamref name="TRequest"/> from the JSON body and returns 204 No Content.</summary>
    /// <typeparam name="TRequest">The mediator request.</typeparam>
    /// <param name="endpoints">The route builder.</param>
    /// <param name="pattern">The route pattern.</param>
    /// <returns>The endpoint builder for further conventions.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="endpoints"/> or <paramref name="pattern"/> is <see langword="null"/>.</exception>
    public static RouteHandlerBuilder MapPatchRequest<TRequest>(
        this IEndpointRouteBuilder endpoints,
        [StringSyntax("Route")] string pattern
    )
        where TRequest : IRequest<ApiResult>
    {
        Argument.IsNotNull(endpoints);
        Argument.IsNotNull(pattern);

        return endpoints.MapPatch(pattern, _FromBody<TRequest>);
    }

    // Static methods instead of lambdas: a method group converts to a Delegate whose parameter attributes the
    // request delegate factory reads the same way, and it allocates no closure per mapping.
    private static ValueTask<ApiResultHttpResult<TResponse>> _FromParameters<TRequest, TResponse>(
        [AsParameters] TRequest request,
        [FromServices] ISender sender,
        CancellationToken cancellationToken
    )
        where TRequest : IRequest<ApiResult<TResponse>>
    {
        return sender.DispatchAsync<TResponse>(request, cancellationToken);
    }

    private static ValueTask<ApiResultHttpResult> _FromParameters<TRequest>(
        [AsParameters] TRequest request,
        [FromServices] ISender sender,
        CancellationToken cancellationToken
    )
        where TRequest : IRequest<ApiResult>
    {
        return sender.DispatchAsync(request, cancellationToken);
    }

    // EmptyBodyBehavior.Allow binds an empty body as null so DispatchAsync answers it with a problem-details 400; the
    // framework default answers a bare 400 before the handler runs.
    private static ValueTask<ApiResultHttpResult<TResponse>> _FromBody<TRequest, TResponse>(
        [FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Allow)] TRequest? request,
        [FromServices] ISender sender,
        CancellationToken cancellationToken
    )
        where TRequest : IRequest<ApiResult<TResponse>>
    {
        return sender.DispatchAsync<TResponse>(request, cancellationToken);
    }

    private static ValueTask<ApiResultHttpResult> _FromBody<TRequest>(
        [FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Allow)] TRequest? request,
        [FromServices] ISender sender,
        CancellationToken cancellationToken
    )
        where TRequest : IRequest<ApiResult>
    {
        return sender.DispatchAsync(request, cancellationToken);
    }
}
