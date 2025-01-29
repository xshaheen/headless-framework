// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Framework.Constants;
using Framework.Primitives;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace Framework.Api.Abstractions;

public interface IProblemDetailsCreator
{
    ProblemDetails EndpointNotFound(HttpContext context);

    ProblemDetails EntityNotFound(HttpContext context, string entity, string key);

    ProblemDetails MalformedSyntax(HttpContext context);

    ProblemDetails UnprocessableEntity(
        HttpContext context,
        IReadOnlyDictionary<string, IReadOnlyList<ErrorDescriptor>> errorDescriptors
    );

    ProblemDetails Conflict(HttpContext context, IEnumerable<ErrorDescriptor> errorDescriptors);

    ProblemDetails InternalError(HttpContext context);
}

public sealed class ProblemDetailsCreator : IProblemDetailsCreator
{
    public ProblemDetails EndpointNotFound(HttpContext context)
    {
        return new ProblemDetails
        {
            Status = StatusCodes.Status404NotFound,
            Title = ProblemDetailTitles.EndpointNotFounded,
            Detail = $"The requested endpoint '{context.Request.Path}' was not found.",
        };
    }

    public ProblemDetails EntityNotFound(HttpContext context, string entity, string key)
    {
        return new ProblemDetails
        {
            Status = StatusCodes.Status404NotFound,
            Title = ProblemDetailTitles.EntityNotFounded,
            Detail = $"The requested entity does not exist. There is no entity matches '{entity}:{key}'.",
            Extensions = { ["params"] = new { entity, key } },
        };
    }

    public ProblemDetails MalformedSyntax(HttpContext context)
    {
        return new ProblemDetails
        {
            Status = StatusCodes.Status400BadRequest,
            Title = ProblemDetailTitles.BadRequest,
            Detail =
                "Failed to parse. The request body is empty or could not be understood by the server due to malformed syntax.",
        };
    }

    public ProblemDetails UnprocessableEntity(
        HttpContext context,
        IReadOnlyDictionary<string, IReadOnlyList<ErrorDescriptor>> errorDescriptors
    )
    {
        return new ProblemDetails
        {
            Title = ProblemDetailTitles.ValidationProblem,
            Status = StatusCodes.Status422UnprocessableEntity,
            Detail = "One or more validation errors occurred.",
            Extensions = { ["errors"] = errorDescriptors },
        };
    }

    public ProblemDetails Conflict(HttpContext context, IEnumerable<ErrorDescriptor> errorDescriptors)
    {
        return new ProblemDetails
        {
            Status = StatusCodes.Status409Conflict,
            Title = ProblemDetailTitles.ConflictRequest,
            Detail = "Conflict request",
            Extensions = { ["errors"] = errorDescriptors },
        };
    }

    public ProblemDetails InternalError(HttpContext context)
    {
        return new ProblemDetails
        {
            Status = StatusCodes.Status500InternalServerError,
            Title = ProblemDetailTitles.UnhandledException,
            Detail = "An error occurred while processing your request.",
        };
    }
}
