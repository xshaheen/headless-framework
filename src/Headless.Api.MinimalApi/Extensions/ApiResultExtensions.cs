// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Api.Abstractions;
using Microsoft.AspNetCore.Http;

// ReSharper disable once CheckNamespace
#pragma warning disable IDE0130
namespace Headless.Primitives;

/// <summary>
/// Extensions to convert <see cref="ApiResult{T}"/> and <see cref="ApiResult"/> discriminated unions
/// to Minimal API <see cref="IResult"/> responses. Error types are mapped to HTTP status codes as follows:
/// <list type="bullet">
///   <item><see cref="NotFoundError"/> → 404 Not Found</item>
///   <item><see cref="ValidationError"/> → 422 Unprocessable Entity</item>
///   <item><see cref="ForbiddenError"/> → 403 Forbidden</item>
///   <item><see cref="UnauthorizedError"/> → 401 Unauthorized</item>
///   <item><see cref="AggregateError"/> → 409 Conflict</item>
///   <item><see cref="ConflictError"/> → 409 Conflict</item>
///   <item>All other errors → 409 Conflict</item>
/// </list>
/// </summary>
[PublicAPI]
public static class ApiResultExtensions
{
    /// <summary>
    /// Converts a valued <see cref="ApiResult{T}"/> to an HTTP response: 200 OK on success,
    /// or the appropriate problem-details response on failure.
    /// </summary>
    /// <typeparam name="T">The success value type.</typeparam>
    /// <param name="result">The result to convert.</param>
    /// <param name="creator">The problem-details creator used to build error responses.</param>
    /// <returns>An <see cref="IResult"/> representing the HTTP response.</returns>
    public static IResult ToHttpResult<T>(this ApiResult<T> result, IProblemDetailsCreator creator)
    {
        // Branch instead of Match: the failure lambda would capture `creator` and allocate on every success response.
        return result.TryGetValue(out var value) ? TypedResults.Ok(value) : result.Error.ToHttpResult(creator);
    }

    /// <summary>
    /// Converts a unit <see cref="ApiResult"/> to an HTTP response: 204 No Content on success,
    /// or the appropriate problem-details response on failure.
    /// </summary>
    /// <param name="result">The result to convert.</param>
    /// <param name="creator">The problem-details creator used to build error responses.</param>
    /// <returns>An <see cref="IResult"/> representing the HTTP response.</returns>
    public static IResult ToHttpResult(this ApiResult result, IProblemDetailsCreator creator)
    {
        // Branch instead of Match: the failure lambda would capture `creator` and allocate on every success response.
        return result.TryGetError(out var error) ? error.ToHttpResult(creator) : TypedResults.NoContent();
    }

    /// <summary>
    /// Maps a <see cref="ResultError"/> to the appropriate problem-details HTTP response using
    /// pattern matching on the concrete error type.
    /// </summary>
    /// <param name="error">The error to map.</param>
    /// <param name="creator">The problem-details creator used to build the response body.</param>
    /// <returns>An <see cref="IResult"/> with the appropriate HTTP status code and problem-details body.</returns>
    public static IResult ToHttpResult(this ResultError error, IProblemDetailsCreator creator)
    {
        return error switch
        {
            NotFoundError => TypedResults.Problem(creator.EntityNotFound()),

            ValidationError e => TypedResults.Problem(creator.UnprocessableEntity(e.ToErrorDescriptorDictionary())),

            ForbiddenError e => TypedResults.Problem(
                creator.Forbidden(error: new ErrorDescriptor("g:forbidden", e.Reason))
            ),

            UnauthorizedError => TypedResults.Problem(creator.Unauthorized()),

            AggregateError e => TypedResults.Problem(
                creator.Conflict(e.Errors.Select(err => new ErrorDescriptor(err.Code, err.Message)).ToList())
            ),

            ConflictError e => TypedResults.Problem(creator.Conflict([new ErrorDescriptor(e.Code, e.Message)])),

            // Default: treat as conflict
            _ => TypedResults.Problem(creator.Conflict([new ErrorDescriptor(error.Code, error.Message)])),
        };
    }
}
