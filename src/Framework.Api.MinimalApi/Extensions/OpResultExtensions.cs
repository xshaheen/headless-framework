// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Framework.Api.Abstractions;
using Framework.Primitives;
using Microsoft.AspNetCore.Http;

namespace Framework.Api.MinimalApi.Extensions;

/// <summary>
/// Extensions to convert OpResult to HTTP responses.
/// Maps error types to appropriate HTTP status codes.
/// </summary>
[PublicAPI]
public static class OpResultExtensions
{
    public static IResult ToHttpResult<T>(this OpResult<T> result, IProblemDetailsCreator creator) =>
        result.Match(value => TypedResults.Ok(value), error => error.ToHttpResult(creator));

    public static IResult ToHttpResult(this OpResult result, IProblemDetailsCreator creator) =>
        result.Match(() => TypedResults.NoContent(), error => error.ToHttpResult(creator));

    /// <summary>
    /// Maps ResultError to HTTP response using pattern matching.
    /// </summary>
    public static IResult ToHttpResult(this ResultError error, IProblemDetailsCreator creator) =>
        error switch
        {
            NotFoundError e => TypedResults.Problem(creator.EntityNotFound(e.Entity, e.Key)),

            ValidationError e => TypedResults.Problem(creator.UnprocessableEntity(_ToErrorDescriptorDict(e))),

            ForbiddenError e => TypedResults.Problem(creator.Forbidden([new ErrorDescriptor("forbidden", e.Reason)])),

            UnauthorizedError => TypedResults.Problem(creator.Unauthorized()),

            AggregateError e => TypedResults.Problem(
                creator.Conflict(e.Errors.Select(err => new ErrorDescriptor(err.Code, err.Message)).ToList())
            ),

            ConflictError e => TypedResults.Problem(creator.Conflict([new ErrorDescriptor(e.Code, e.Message)])),

            // Default: treat as conflict
            _ => TypedResults.Problem(creator.Conflict([new ErrorDescriptor(error.Code, error.Message)])),
        };

    private static Dictionary<string, List<ErrorDescriptor>> _ToErrorDescriptorDict(ValidationError e) =>
        e.FieldErrors.ToDictionary(
            kv => kv.Key,
            kv => kv.Value.Select(msg => new ErrorDescriptor($"validation:{kv.Key}", msg)).ToList(),
            StringComparer.Ordinal
        );
}
