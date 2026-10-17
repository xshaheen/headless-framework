// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Primitives;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace Headless.Api;

/// <summary>
/// The one mapping from an <see cref="ApiResultError"/> to its problem-details response, shared by the Minimal API and
/// MVC result conversions, so the two never disagree on which status an error kind gets.
/// </summary>
/// <remarks>
/// <see cref="NotFoundError"/> 404, <see cref="ValidationError"/> 422, <see cref="ForbiddenError"/> 403,
/// <see cref="UnauthorizedError"/> 401, an <see cref="AggregateError"/> of only validation errors 422, any other
/// <see cref="AggregateError"/> 409, <see cref="ConflictError"/> 409, and every other error 409. The status is the
/// returned <see cref="ProblemDetails.Status"/>.
/// </remarks>
internal static class ApiResultErrorProblemDetails
{
    public static ProblemDetails Create(ApiResultError error, IProblemDetailsCreator creator)
    {
        return error switch
        {
            NotFoundError => creator.EntityNotFound(),
            ValidationError e => creator.UnprocessableEntity(e.Errors),
            ForbiddenError e => _UseErrorAsDetail(creator.Forbidden(error: e.Error)),
            UnauthorizedError e => _UseErrorAsDetail(creator.Unauthorized(error: e.Error)),
            AggregateError e when e.TryGetValidationErrors(out var validationErrors) => creator.UnprocessableEntity(
                validationErrors
            ),
            AggregateError e => creator.Conflict(e.ToErrorDescriptors()),
            ConflictError e => creator.Conflict(e.Errors),
            _ => creator.Conflict([error.ToErrorDescriptor()]),
        };
    }

    /// <summary>
    /// The status <see cref="Create"/> gives <paramref name="error"/>, known without building the body, so a result
    /// can report its status before a request supplies the <see cref="IProblemDetailsCreator"/>.
    /// </summary>
    public static int GetStatusCode(ApiResultError error)
    {
        return error switch
        {
            NotFoundError => StatusCodes.Status404NotFound,
            ValidationError => StatusCodes.Status422UnprocessableEntity,
            ForbiddenError => StatusCodes.Status403Forbidden,
            UnauthorizedError => StatusCodes.Status401Unauthorized,
            AggregateError e when e.TryGetValidationErrors(out _) => StatusCodes.Status422UnprocessableEntity,
            _ => StatusCodes.Status409Conflict,
        };
    }

    // A denial that names its reason should say it in `detail`, the field clients show, instead of the generic
    // "forbidden" text. The descriptor is read back from the extension after Normalize so the detail carries the
    // localized description, not the one the handler wrote.
    private static ProblemDetails _UseErrorAsDetail(ProblemDetails problemDetails)
    {
        if (problemDetails.Extensions.TryGetValue("error", out var value) && value is ErrorDescriptor descriptor)
        {
            problemDetails.Detail = descriptor.Description;
        }

        return problemDetails;
    }
}
