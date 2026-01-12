// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Framework.Api.Abstractions;
using Microsoft.AspNetCore.Http;

// ReSharper disable once CheckNamespace
#pragma warning disable IDE0130
namespace Framework.Primitives;

/// <summary>
/// Extensions to convert OpResult to HTTP responses.
/// Maps error types to appropriate HTTP status codes.
/// </summary>
[PublicAPI]
public static class ApiResultExtensions
{
    public static IResult ToHttpResult<T>(this ApiResult<T> result, IProblemDetailsCreator creator)
    {
        return result.Match(TypedResults.Ok, error => error.ToHttpResult(creator));
    }

    public static IResult ToHttpResult(this ApiResult result, IProblemDetailsCreator creator)
    {
        return result.Match(TypedResults.NoContent, error => error.ToHttpResult(creator));
    }

    /// <summary>
    /// Maps ResultError to HTTP response using pattern matching.
    /// </summary>
    public static IResult ToHttpResult(this ResultError error, IProblemDetailsCreator creator)
    {
        return error switch
        {
            NotFoundError e => TypedResults.Problem(creator.EntityNotFound(e.Entity, e.Key)),

            ValidationError e => TypedResults.Problem(creator.UnprocessableEntity(e.ToErrorDescriptorDictionary())),

            ForbiddenError e => TypedResults.Problem(creator.Forbidden([new ErrorDescriptor("forbidden", e.Reason)])),

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
