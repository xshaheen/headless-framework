// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;
using Headless.Primitives;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.Extensions.DependencyInjection;

namespace Headless.Api;

/// <summary>
/// Extensions to convert <see cref="ApiResult{T}"/> and <see cref="ApiResult"/> discriminated unions
/// to Minimal API <see cref="IResult"/> responses. Error types are mapped to HTTP status codes as follows:
/// <list type="bullet">
///   <item><see cref="NotFoundError"/> → 404 Not Found</item>
///   <item><see cref="ValidationError"/> → 422 Unprocessable Entity</item>
///   <item><see cref="ForbiddenError"/> → 403 Forbidden, with the error's description as the <c>detail</c></item>
///   <item><see cref="UnauthorizedError"/> → 401 Unauthorized, with a carried error's description as the <c>detail</c></item>
///   <item><see cref="AggregateError"/> containing only validation errors → 422 Unprocessable Entity</item>
///   <item>Other <see cref="AggregateError"/> instances → 409 Conflict</item>
///   <item><see cref="ConflictError"/> → 409 Conflict</item>
///   <item>All other errors → 409 Conflict</item>
/// </list>
/// </summary>
/// <remarks>
/// The <see cref="ApiResult"/> conversions build the problem body when the result executes, from the request's
/// <see cref="IProblemDetailsCreator"/>, so an endpoint returns them without injecting the creator.
/// </remarks>
[PublicAPI]
public static class ApiResultExtensions
{
    /// <summary>
    /// Converts a valued <see cref="ApiResult{T}"/> to an HTTP response: 200 OK on success,
    /// or the appropriate problem-details response on failure.
    /// </summary>
    /// <typeparam name="T">The success value type.</typeparam>
    /// <param name="result">The result to convert.</param>
    /// <returns>An OpenAPI-aware HTTP result representing the response.</returns>
    /// <exception cref="InvalidOperationException"><paramref name="result"/> is <see langword="default"/>.</exception>
    public static ApiResultHttpResult<T> ToHttpResult<T>(this ApiResult<T> result)
    {
        return new ApiResultHttpResult<T>(result);
    }

    /// <summary>
    /// Converts a valued <see cref="ApiResult{T}"/> to an HTTP response: 201 Created with a <c>Location</c> field on
    /// success, or the appropriate problem-details response on failure.
    /// </summary>
    /// <typeparam name="T">The created resource type.</typeparam>
    /// <param name="result">The result to convert.</param>
    /// <param name="location">Builds the created resource's URI from the success value; called only on success.</param>
    /// <returns>An OpenAPI-aware HTTP result representing the response.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="location"/> is <see langword="null"/>.</exception>
    /// <exception cref="InvalidOperationException"><paramref name="result"/> is <see langword="default"/>.</exception>
    public static ApiResultCreatedHttpResult<T> ToCreatedHttpResult<T>(
        this ApiResult<T> result,
        Func<T, string> location
    )
    {
        return new ApiResultCreatedHttpResult<T>(result, location);
    }

    /// <summary>
    /// Converts a valued <see cref="ApiResult{T}"/> to an HTTP response: 202 Accepted with the value on success, or
    /// the appropriate problem-details response on failure. Use it when the work continues after the response.
    /// </summary>
    /// <typeparam name="T">The accepted value type, typically a status or tracking resource.</typeparam>
    /// <param name="result">The result to convert.</param>
    /// <returns>An OpenAPI-aware HTTP result representing the response.</returns>
    /// <exception cref="InvalidOperationException"><paramref name="result"/> is <see langword="default"/>.</exception>
    public static ApiResultAcceptedHttpResult<T> ToAcceptedHttpResult<T>(this ApiResult<T> result)
    {
        return new ApiResultAcceptedHttpResult<T>(result, location: null);
    }

    /// <summary>
    /// Converts a valued <see cref="ApiResult{T}"/> to an HTTP response: 202 Accepted with the value and a
    /// <c>Location</c> field on success, or the appropriate problem-details response on failure.
    /// </summary>
    /// <typeparam name="T">The accepted value type, typically a status or tracking resource.</typeparam>
    /// <param name="result">The result to convert.</param>
    /// <param name="location">Builds the status monitor's URI from the success value; called only on success.</param>
    /// <returns>An OpenAPI-aware HTTP result representing the response.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="location"/> is <see langword="null"/>.</exception>
    /// <exception cref="InvalidOperationException"><paramref name="result"/> is <see langword="default"/>.</exception>
    public static ApiResultAcceptedHttpResult<T> ToAcceptedHttpResult<T>(
        this ApiResult<T> result,
        Func<T, string> location
    )
    {
        Argument.IsNotNull(location);

        return new ApiResultAcceptedHttpResult<T>(result, location);
    }

    /// <summary>
    /// Converts a unit <see cref="ApiResult"/> to an HTTP response: 202 Accepted without a body on success, or the
    /// appropriate problem-details response on failure. Use it when the work continues after the response.
    /// </summary>
    /// <param name="result">The result to convert.</param>
    /// <returns>An OpenAPI-aware HTTP result representing the response.</returns>
    /// <exception cref="InvalidOperationException"><paramref name="result"/> is <see langword="default"/>.</exception>
    public static ApiResultAcceptedHttpResult ToAcceptedHttpResult(this ApiResult result)
    {
        return new ApiResultAcceptedHttpResult(result, location: null);
    }

    /// <summary>
    /// Converts a unit <see cref="ApiResult"/> to an HTTP response: 202 Accepted without a body and with a
    /// <c>Location</c> field on success, or the appropriate problem-details response on failure.
    /// </summary>
    /// <param name="result">The result to convert.</param>
    /// <param name="location">The status monitor's URI; written only on success.</param>
    /// <returns>An OpenAPI-aware HTTP result representing the response.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="location"/> is <see langword="null"/>.</exception>
    /// <exception cref="InvalidOperationException"><paramref name="result"/> is <see langword="default"/>.</exception>
    public static ApiResultAcceptedHttpResult ToAcceptedHttpResult(this ApiResult result, string location)
    {
        Argument.IsNotNull(location);

        return new ApiResultAcceptedHttpResult(result, location);
    }

    /// <summary>
    /// Converts a unit <see cref="ApiResult"/> to an HTTP response: 204 No Content on success,
    /// or the appropriate problem-details response on failure.
    /// </summary>
    /// <param name="result">The result to convert.</param>
    /// <returns>An OpenAPI-aware HTTP result representing the response.</returns>
    /// <exception cref="InvalidOperationException"><paramref name="result"/> is <see langword="default"/>.</exception>
    public static ApiResultHttpResult ToHttpResult(this ApiResult result)
    {
        return new ApiResultHttpResult(result);
    }

    /// <summary>
    /// Maps a <see cref="ApiResultError"/> to the appropriate problem-details HTTP response using
    /// pattern matching on the concrete error type. Use it to compose a custom <c>Results&lt;...&gt;</c> union.
    /// </summary>
    /// <param name="error">The error to map.</param>
    /// <param name="httpContext">
    /// The current request; its services supply the <see cref="IProblemDetailsCreator"/> and the application's
    /// <see cref="HeadlessProblemDetailsOptions"/> mappings.
    /// </param>
    /// <returns>A problem HTTP result with the appropriate status code and body.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="error"/> or <paramref name="httpContext"/> is <see langword="null"/>.</exception>
    public static ProblemHttpResult ToHttpResult(this ApiResultError error, HttpContext httpContext)
    {
        Argument.IsNotNull(error);
        Argument.IsNotNull(httpContext);

        var services = httpContext.RequestServices;
        var problemDetails = ApiResultErrorProblemDetails.Create(
            error,
            services.GetRequiredService<IProblemDetailsCreator>(),
            ApiResultErrorProblemDetails.GetMappings(services)
        );

        return TypedResults.Problem(problemDetails);
    }
}
