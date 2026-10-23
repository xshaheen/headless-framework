// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;
using Headless.Primitives;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace Headless.Api;

/// <summary>
/// Extensions to convert <see cref="ApiResult{T}"/> and <see cref="ApiResult"/> discriminated unions
/// to MVC <see cref="ActionResult"/> responses. Error types are mapped to HTTP status codes as follows:
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
    /// <returns>An OpenAPI-aware action result representing the HTTP response.</returns>
    public static ApiActionResult<T> ToActionResult<T>(
        this ApiResult<T> result,
        ControllerBase controller,
        IProblemDetailsCreator creator
    )
    {
        // Branch instead of Match: both lambdas would capture `controller`/`creator` and allocate on every response.
        return new ApiActionResult<T>(
            result.TryGetValue(out var value) ? controller.Ok(value) : result.Error.ToActionResult(controller, creator)
        );
    }

    /// <summary>
    /// Converts a valued <see cref="ApiResult{T}"/> to an MVC action result: 201 Created with the value and a
    /// <c>Location</c> field on success, or the appropriate problem-details response on failure.
    /// </summary>
    /// <typeparam name="T">The created resource type.</typeparam>
    /// <param name="result">The result to convert.</param>
    /// <param name="controller">The controller instance used to create action results.</param>
    /// <param name="creator">The problem-details creator used to build error responses.</param>
    /// <param name="location">Builds the created resource's URI from the success value; called only on success.</param>
    /// <returns>An OpenAPI-aware action result representing the HTTP response.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="location"/> is <see langword="null"/>.</exception>
    public static ApiCreatedActionResult<T> ToCreatedActionResult<T>(
        this ApiResult<T> result,
        ControllerBase controller,
        IProblemDetailsCreator creator,
        Func<T, string> location
    )
    {
        Argument.IsNotNull(location);

        return new ApiCreatedActionResult<T>(
            result.TryGetValue(out var value)
                ? controller.Created(location(value), value)
                : result.Error.ToActionResult(controller, creator)
        );
    }

    /// <summary>
    /// Converts a valued <see cref="ApiResult{T}"/> to an MVC action result: 202 Accepted with the value on success,
    /// or the appropriate problem-details response on failure. Use it when the work continues after the response.
    /// </summary>
    /// <typeparam name="T">The accepted value type, typically a status or tracking resource.</typeparam>
    /// <param name="result">The result to convert.</param>
    /// <param name="controller">The controller instance used to create action results.</param>
    /// <param name="creator">The problem-details creator used to build error responses.</param>
    /// <returns>An OpenAPI-aware action result representing the HTTP response.</returns>
    public static ApiAcceptedActionResult<T> ToAcceptedActionResult<T>(
        this ApiResult<T> result,
        ControllerBase controller,
        IProblemDetailsCreator creator
    )
    {
        return new ApiAcceptedActionResult<T>(
            result.TryGetValue(out var value)
                // Pass the URI explicitly: Accepted(value) binds to Accepted(string uri) when T is string.
                ? controller.Accepted(uri: (string?)null, value)
                : result.Error.ToActionResult(controller, creator)
        );
    }

    /// <summary>
    /// Converts a valued <see cref="ApiResult{T}"/> to an MVC action result: 202 Accepted with the value and a
    /// <c>Location</c> field on success, or the appropriate problem-details response on failure.
    /// </summary>
    /// <typeparam name="T">The accepted value type, typically a status or tracking resource.</typeparam>
    /// <param name="result">The result to convert.</param>
    /// <param name="controller">The controller instance used to create action results.</param>
    /// <param name="creator">The problem-details creator used to build error responses.</param>
    /// <param name="location">Builds the status monitor's URI from the success value; called only on success.</param>
    /// <returns>An OpenAPI-aware action result representing the HTTP response.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="location"/> is <see langword="null"/>.</exception>
    public static ApiAcceptedActionResult<T> ToAcceptedActionResult<T>(
        this ApiResult<T> result,
        ControllerBase controller,
        IProblemDetailsCreator creator,
        Func<T, string> location
    )
    {
        Argument.IsNotNull(location);

        return new ApiAcceptedActionResult<T>(
            result.TryGetValue(out var value)
                ? controller.Accepted(location(value), value)
                : result.Error.ToActionResult(controller, creator)
        );
    }

    /// <summary>
    /// Converts a unit <see cref="ApiResult"/> to an MVC action result: 202 Accepted without a body on success, or
    /// the appropriate problem-details response on failure. Use it when the work continues after the response.
    /// </summary>
    /// <param name="result">The result to convert.</param>
    /// <param name="controller">The controller instance used to create action results.</param>
    /// <param name="creator">The problem-details creator used to build error responses.</param>
    /// <returns>An OpenAPI-aware action result representing the HTTP response.</returns>
    public static ApiAcceptedActionResult ToAcceptedActionResult(
        this ApiResult result,
        ControllerBase controller,
        IProblemDetailsCreator creator
    )
    {
        return new ApiAcceptedActionResult(
            result.IsSuccess ? controller.Accepted() : result.Error.ToActionResult(controller, creator)
        );
    }

    /// <summary>
    /// Converts a unit <see cref="ApiResult"/> to an MVC action result: 202 Accepted without a body and with a
    /// <c>Location</c> field on success, or the appropriate problem-details response on failure.
    /// </summary>
    /// <param name="result">The result to convert.</param>
    /// <param name="controller">The controller instance used to create action results.</param>
    /// <param name="creator">The problem-details creator used to build error responses.</param>
    /// <param name="location">The status monitor's URI; written only on success.</param>
    /// <returns>An OpenAPI-aware action result representing the HTTP response.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="location"/> is <see langword="null"/>.</exception>
    public static ApiAcceptedActionResult ToAcceptedActionResult(
        this ApiResult result,
        ControllerBase controller,
        IProblemDetailsCreator creator,
        string location
    )
    {
        Argument.IsNotNull(location);

        return new ApiAcceptedActionResult(
            result.IsSuccess ? controller.Accepted(location) : result.Error.ToActionResult(controller, creator)
        );
    }

    /// <summary>
    /// Converts a unit <see cref="ApiResult"/> to an MVC action result: 204 No Content on success,
    /// or the appropriate problem-details response on failure.
    /// </summary>
    /// <param name="result">The result to convert.</param>
    /// <param name="controller">The controller instance used to create action results.</param>
    /// <param name="creator">The problem-details creator used to build error responses.</param>
    /// <returns>An OpenAPI-aware action result representing the HTTP response.</returns>
    public static ApiActionResult ToActionResult(
        this ApiResult result,
        ControllerBase controller,
        IProblemDetailsCreator creator
    )
    {
        // Branch instead of Match: the delegates would capture `controller`/`creator` and allocate on every response.
        return new ApiActionResult(
            result.IsSuccess ? controller.NoContent() : result.Error.ToActionResult(controller, creator)
        );
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
        var problemDetails = ApiResultErrorProblemDetails.Create(
            error,
            creator,
            ApiResultErrorProblemDetails.GetMappings(controller.HttpContext?.RequestServices)
        );

        // The controller helpers keep the typed results (ConflictObjectResult, ...) callers and filters may match on.
        return problemDetails.Status switch
        {
            StatusCodes.Status404NotFound => controller.NotFound(problemDetails),
            StatusCodes.Status422UnprocessableEntity => controller.UnprocessableEntity(problemDetails),
            StatusCodes.Status401Unauthorized => controller.Unauthorized(problemDetails),
            StatusCodes.Status409Conflict => controller.Conflict(problemDetails),
            _ => new ObjectResult(problemDetails) { StatusCode = problemDetails.Status },
        };
    }
}
