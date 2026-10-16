// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;
using Headless.Primitives;
using Mediator;

namespace Headless.Api;

/// <summary>
/// Dispatches a mediator request and converts its <see cref="ApiResult"/> to an OpenAPI-aware Minimal API result in
/// one call, so a route handler stays a one-liner with no mapping logic at the edge.
/// </summary>
/// <remarks>
/// A <see langword="null"/> request, which is what a nullable body parameter binds from an empty body, answers
/// 400 Bad Request without reaching the mediator, as the MVC controller helpers do. The problem body is built when
/// the result executes, from the request's <see cref="IProblemDetailsCreator"/>.
/// </remarks>
[PublicAPI]
public static class HeadlessSenderExtensions
{
    /// <summary>
    /// Sends <paramref name="request"/> and returns 204 No Content on success, or the error's problem-details
    /// response.
    /// </summary>
    /// <param name="sender">The mediator sender.</param>
    /// <param name="request">The request to send, or <see langword="null"/> when binding produced none.</param>
    /// <param name="cancellationToken">The request-aborted token.</param>
    /// <returns>The HTTP result for the handler's outcome.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="sender"/> is <see langword="null"/>.</exception>
    public static async ValueTask<ApiResultHttpResult> DispatchAsync(
        this ISender sender,
        IRequest<ApiResult>? request,
        CancellationToken cancellationToken = default
    )
    {
        Argument.IsNotNull(sender);

        if (request is null)
        {
            return new ApiResultHttpResult(MalformedRequestError.Instance);
        }

        var result = await sender.Send(request, cancellationToken).ConfigureAwait(false);

        return new ApiResultHttpResult(result);
    }

    /// <summary>
    /// Sends <paramref name="request"/> and returns 200 OK with the handler's value on success, or the error's
    /// problem-details response.
    /// </summary>
    /// <typeparam name="T">The success value type.</typeparam>
    /// <param name="sender">The mediator sender.</param>
    /// <param name="request">The request to send, or <see langword="null"/> when binding produced none.</param>
    /// <param name="cancellationToken">The request-aborted token.</param>
    /// <returns>The HTTP result for the handler's outcome.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="sender"/> is <see langword="null"/>.</exception>
    public static async ValueTask<ApiResultHttpResult<T>> DispatchAsync<T>(
        this ISender sender,
        IRequest<ApiResult<T>>? request,
        CancellationToken cancellationToken = default
    )
    {
        Argument.IsNotNull(sender);

        if (request is null)
        {
            return new ApiResultHttpResult<T>(MalformedRequestError.Instance);
        }

        var result = await sender.Send(request, cancellationToken).ConfigureAwait(false);

        return new ApiResultHttpResult<T>(result);
    }

    /// <summary>
    /// Sends <paramref name="request"/> and returns 201 Created with the handler's value and a <c>Location</c> field
    /// on success, or the error's problem-details response.
    /// </summary>
    /// <typeparam name="T">The created resource type.</typeparam>
    /// <param name="sender">The mediator sender.</param>
    /// <param name="request">The request to send, or <see langword="null"/> when binding produced none.</param>
    /// <param name="location">Builds the created resource's URI from the success value; called only on success.</param>
    /// <param name="cancellationToken">The request-aborted token.</param>
    /// <returns>The HTTP result for the handler's outcome.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="sender"/> or <paramref name="location"/> is <see langword="null"/>.</exception>
    public static async ValueTask<ApiResultCreatedHttpResult<T>> DispatchCreatedAsync<T>(
        this ISender sender,
        IRequest<ApiResult<T>>? request,
        Func<T, string> location,
        CancellationToken cancellationToken = default
    )
    {
        Argument.IsNotNull(sender);
        Argument.IsNotNull(location);

        if (request is null)
        {
            return new ApiResultCreatedHttpResult<T>(MalformedRequestError.Instance);
        }

        var result = await sender.Send(request, cancellationToken).ConfigureAwait(false);

        return new ApiResultCreatedHttpResult<T>(result, location);
    }
}
