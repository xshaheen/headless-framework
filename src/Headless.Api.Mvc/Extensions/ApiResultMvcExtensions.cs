// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Api.Abstractions;
using Headless.Primitives;
using Microsoft.AspNetCore.Mvc;

// ReSharper disable once CheckNamespace
#pragma warning disable IDE0130
namespace Headless.Primitives;

/// <summary>
/// Extensions to convert OpResult to MVC ActionResult responses.
/// </summary>
[PublicAPI]
public static class ApiResultMvcExtensions
{
    public static ActionResult<T> ToActionResult<T>(
        this ApiResult<T> result,
        ControllerBase controller,
        IProblemDetailsCreator creator
    )
    {
        return result.Match<ActionResult<T>>(
            value => controller.Ok(value),
            error => error.ToActionResult(controller, creator)
        );
    }

    public static ActionResult ToActionResult(
        this ApiResult result,
        ControllerBase controller,
        IProblemDetailsCreator creator
    )
    {
        return result.Match(controller.NoContent, error => error.ToActionResult(controller, creator));
    }

    /// <summary>
    /// Maps ResultError to MVC ActionResult using pattern matching.
    /// </summary>
    public static ActionResult ToActionResult(
        this ResultError error,
        ControllerBase controller,
        IProblemDetailsCreator creator
    )
    {
        return error switch
        {
            NotFoundError e => controller.NotFound(creator.EntityNotFound(e.Entity, e.Key)),

            ValidationError e => controller.UnprocessableEntity(
                creator.UnprocessableEntity(e.ToErrorDescriptorDictionary())
            ),

            ForbiddenError e => new ObjectResult(creator.Forbidden([new ErrorDescriptor("forbidden", e.Reason)]))
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
