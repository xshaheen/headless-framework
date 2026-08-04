// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Abstractions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;

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
///   <item><see cref="AggregateError"/> containing only validation errors → 422 Unprocessable Entity</item>
///   <item>Other <see cref="AggregateError"/> instances → 409 Conflict</item>
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
    /// <returns>An OpenAPI-aware HTTP result representing the response.</returns>
    public static ApiResultHttpResult<T> ToHttpResult<T>(this ApiResult<T> result, IProblemDetailsCreator creator)
    {
        return new ApiResultHttpResult<T>(result, creator);
    }

    /// <summary>
    /// Converts a unit <see cref="ApiResult"/> to an HTTP response: 204 No Content on success,
    /// or the appropriate problem-details response on failure.
    /// </summary>
    /// <param name="result">The result to convert.</param>
    /// <param name="creator">The problem-details creator used to build error responses.</param>
    /// <returns>An OpenAPI-aware HTTP result representing the response.</returns>
    public static ApiResultHttpResult ToHttpResult(this ApiResult result, IProblemDetailsCreator creator)
    {
        return new ApiResultHttpResult(result, creator);
    }

    /// <summary>
    /// Maps a <see cref="ApiResultError"/> to the appropriate problem-details HTTP response using
    /// pattern matching on the concrete error type.
    /// </summary>
    /// <param name="error">The error to map.</param>
    /// <param name="creator">The problem-details creator used to build the response body.</param>
    /// <returns>A problem HTTP result with the appropriate status code and body.</returns>
    public static ProblemHttpResult ToHttpResult(this ApiResultError error, IProblemDetailsCreator creator)
    {
        return error switch
        {
            NotFoundError => TypedResults.Problem(creator.EntityNotFound()),

            ValidationError e => TypedResults.Problem(creator.UnprocessableEntity(e.Errors)),

            ForbiddenError e => TypedResults.Problem(creator.Forbidden(error: e.Error)),

            UnauthorizedError e => TypedResults.Problem(creator.Unauthorized(e.Error)),

            AggregateError e when e.TryGetValidationErrors(out var validationErrors) => TypedResults.Problem(
                creator.UnprocessableEntity(validationErrors)
            ),

            AggregateError e => TypedResults.Problem(creator.Conflict(e.ToErrorDescriptors())),

            ConflictError e => TypedResults.Problem(creator.Conflict(e.Errors)),

            // Default: treat as conflict
            _ => TypedResults.Problem(creator.Conflict([new ErrorDescriptor(error.Code, error.Message)])),
        };
    }
}
