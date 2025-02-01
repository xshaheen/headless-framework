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

    ProblemDetails UnprocessableEntity(HttpContext context, Dictionary<string, List<ErrorDescriptor>> errors);

    ProblemDetails Conflict(HttpContext context, IEnumerable<ErrorDescriptor> errors);

    ProblemDetails Forbidden(HttpContext context, IEnumerable<ErrorDescriptor> errors);
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

    public ProblemDetails UnprocessableEntity(HttpContext context, Dictionary<string, List<ErrorDescriptor>> errors)
    {
        return new ProblemDetails
        {
            Title = ProblemDetailTitles.ValidationProblem,
            Status = StatusCodes.Status422UnprocessableEntity,
            Detail = "One or more validation errors occurred.",
            Extensions = { ["errors"] = errors },
        };
    }

    public ProblemDetails Conflict(HttpContext context, IEnumerable<ErrorDescriptor> errors)
    {
        return new ProblemDetails
        {
            Status = StatusCodes.Status409Conflict,
            Title = ProblemDetailTitles.ConflictRequest,
            Detail = "Conflict request",
            Extensions = { ["errors"] = errors },
        };
    }

    public ProblemDetails Forbidden(HttpContext context, IEnumerable<ErrorDescriptor> errors)
    {
        return new ProblemDetails
        {
            Status = StatusCodes.Status403Forbidden,
            Title = ProblemDetailTitles.ForbiddenRequest,
            Detail = "Forbidden request",
            Extensions = { ["errors"] = errors },
        };
    }
}
