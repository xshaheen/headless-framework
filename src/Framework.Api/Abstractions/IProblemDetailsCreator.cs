// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Framework.Constants;
using Framework.Primitives;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace Framework.Api.Abstractions;

public interface IProblemDetailsCreator
{
    ProblemDetails EndpointNotFound();

    ProblemDetails EntityNotFound(string entity, string key);

    ProblemDetails MalformedSyntax();

    ProblemDetails UnprocessableEntity(Dictionary<string, List<ErrorDescriptor>> errors);

    ProblemDetails Conflict(IEnumerable<ErrorDescriptor> errors);

    ProblemDetails Forbidden(IEnumerable<ErrorDescriptor> errors);
}

public sealed class ProblemDetailsCreator : IProblemDetailsCreator
{
    public ProblemDetails EndpointNotFound()
    {
        return new ProblemDetails
        {
            Status = StatusCodes.Status404NotFound,
            Title = ProblemDetailTitles.EndpointNotFounded,
            Detail = "The requested endpoint was not found.",
        };
    }

    public ProblemDetails EntityNotFound(string entity, string key)
    {
        return new ProblemDetails
        {
            Status = StatusCodes.Status404NotFound,
            Title = ProblemDetailTitles.EntityNotFounded,
            Detail = $"The requested entity does not exist. There is no entity matches '{entity}:{key}'.",
            Extensions = { ["params"] = new { entity, key } },
        };
    }

    public ProblemDetails MalformedSyntax()
    {
        return new ProblemDetails
        {
            Status = StatusCodes.Status400BadRequest,
            Title = ProblemDetailTitles.BadRequest,
            Detail =
                "Failed to parse. The request body is empty or could not be understood by the server due to malformed syntax.",
        };
    }

    public ProblemDetails UnprocessableEntity(Dictionary<string, List<ErrorDescriptor>> errors)
    {
        return new ProblemDetails
        {
            Title = ProblemDetailTitles.ValidationProblem,
            Status = StatusCodes.Status422UnprocessableEntity,
            Detail = "One or more validation errors occurred.",
            Extensions = { ["errors"] = errors },
        };
    }

    public ProblemDetails Conflict(IEnumerable<ErrorDescriptor> errors)
    {
        return new ProblemDetails
        {
            Status = StatusCodes.Status409Conflict,
            Title = ProblemDetailTitles.ConflictRequest,
            Detail = "Conflict request",
            Extensions = { ["errors"] = errors },
        };
    }

    public ProblemDetails Forbidden(IEnumerable<ErrorDescriptor> errors)
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
