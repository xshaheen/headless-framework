// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Primitives;
using Microsoft.AspNetCore.Mvc;

namespace Headless.Api;

/// <summary>
/// The one mapping from an <see cref="ApiResultError"/> to its problem-details response, shared by the Minimal API and
/// MVC result conversions and by the authorization rejection of described requirements, so the three never disagree
/// on which status an error kind gets.
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
            ForbiddenError e => creator.Forbidden(error: e.Error),
            UnauthorizedError e => creator.Unauthorized(e.Error),
            AggregateError e when e.TryGetValidationErrors(out var validationErrors) => creator.UnprocessableEntity(
                validationErrors
            ),
            AggregateError e => creator.Conflict(e.ToErrorDescriptors()),
            ConflictError e => creator.Conflict(e.Errors),
            _ => creator.Conflict([error.ToErrorDescriptor()]),
        };
    }
}
