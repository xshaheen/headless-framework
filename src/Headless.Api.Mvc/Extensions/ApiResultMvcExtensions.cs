// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Abstractions;
using Headless.Api.Resources;
using Microsoft.AspNetCore.Mvc;

// ReSharper disable once CheckNamespace
#pragma warning disable IDE0130
namespace Headless.Primitives;

/// <summary>
/// Extensions to convert <see cref="ApiResult{T}"/> and <see cref="ApiResult"/> discriminated unions
/// to MVC <see cref="ActionResult"/> responses. Error types are mapped to HTTP status codes as follows:
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
public static class ApiResultMvcExtensions
{
    /// <summary>
    /// Converts a valued <see cref="ApiResult{T}"/> to an MVC action result: 200 OK on success,
    /// or the appropriate problem-details response on failure.
    /// </summary>
    /// <typeparam name="T">The success value type.</typeparam>
    /// <param name="result">The result to convert.</param>
    /// <param name="controller">The controller instance used to create action results.</param>
    /// <param name="creator">The problem-details creator used to build error responses.</param>
    /// <returns>An <see cref="ActionResult{T}"/> representing the HTTP response.</returns>
    public static ActionResult<T> ToActionResult<T>(
        this ApiResult<T> result,
        ControllerBase controller,
        IProblemDetailsCreator creator
    )
    {
        // Branch instead of Match: both lambdas would capture `controller`/`creator` and allocate on every response.
        return result.TryGetValue(out var value)
            ? controller.Ok(value)
            : result.Error.ToActionResult(controller, creator);
    }

    /// <summary>
    /// Converts a unit <see cref="ApiResult"/> to an MVC action result: 204 No Content on success,
    /// or the appropriate problem-details response on failure.
    /// </summary>
    /// <param name="result">The result to convert.</param>
    /// <param name="controller">The controller instance used to create action results.</param>
    /// <param name="creator">The problem-details creator used to build error responses.</param>
    /// <returns>An <see cref="ActionResult"/> representing the HTTP response.</returns>
    public static ActionResult ToActionResult(
        this ApiResult result,
        ControllerBase controller,
        IProblemDetailsCreator creator
    )
    {
        // Branch instead of Match: the delegates would capture `controller`/`creator` and allocate on every response.
        return result.TryGetError(out var error) ? error.ToActionResult(controller, creator) : controller.NoContent();
    }

    /// <summary>
    /// Maps a <see cref="ApiResultError"/> to the appropriate problem-details MVC action result using
    /// pattern matching on the concrete error type.
    /// </summary>
    /// <param name="error">The error to map.</param>
    /// <param name="controller">The controller instance used to create action results.</param>
    /// <param name="creator">The problem-details creator used to build the response body.</param>
    /// <returns>An <see cref="ActionResult"/> with the appropriate HTTP status code and problem-details body.</returns>
    public static ActionResult ToActionResult(
        this ApiResultError error,
        ControllerBase controller,
        IProblemDetailsCreator creator
    )
    {
        return error switch
        {
            NotFoundError => controller.NotFound(creator.EntityNotFound()),

            ValidationError e => controller.UnprocessableEntity(
                creator.UnprocessableEntity(e.ToErrorDescriptorDictionary())
            ),

            ForbiddenError e => new ObjectResult(
                creator.Forbidden(error: new ErrorDescriptor(GeneralErrorCodes.Forbidden, e.Reason))
            )
            {
                StatusCode = 403,
            },

            UnauthorizedError => controller.Unauthorized(creator.Unauthorized()),

            AggregateError e => controller.Conflict(
                creator.Conflict(e.Errors.Select(err => new ErrorDescriptor(err.Code, err.Message)).ToList())
            ),

            ConflictError e => controller.Conflict(creator.Conflict([new ErrorDescriptor(e.Code, e.Message)])),

            // Default: treat as conflict
            _ => controller.Conflict(creator.Conflict([new ErrorDescriptor(error.Code, error.Message)])),
        };
    }
}
