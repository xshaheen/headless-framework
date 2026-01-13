// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Diagnostics;
using Framework.Abstractions;
using Framework.Checks;
using Framework.Constants;
using Framework.Primitives;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace Framework.Api.Abstractions;

public interface IProblemDetailsCreator
{
    ProblemDetails EndpointNotFound();

    ProblemDetails EntityNotFound(string entity, string key);

    ProblemDetails MalformedSyntax();

    ProblemDetails TooManyRequests(int retryAfterSeconds);

    ProblemDetails UnprocessableEntity(Dictionary<string, List<ErrorDescriptor>> errors);

    ProblemDetails Conflict(params IEnumerable<ErrorDescriptor> errors);

    ProblemDetails Unauthorized();

    ProblemDetails Forbidden(params IReadOnlyCollection<ErrorDescriptor> errors);

    void Normalize(ProblemDetails problemDetails);
}

public sealed class ProblemDetailsCreator(
    TimeProvider timeProvider,
    IBuildInformationAccessor buildInformationAccessor,
    IHttpContextAccessor httpContextAccessor,
    IOptions<ApiBehaviorOptions> apiOptionsAccessor,
    IOptions<ProblemDetailsOptions>? problemOptionsAccessor = null
) : IProblemDetailsCreator
{
    public ProblemDetails EndpointNotFound()
    {
        var problemDetails = new ProblemDetails
        {
            Status = StatusCodes.Status404NotFound,
            Title = HeadlessProblemDetailsConstants.Titles.EndpointNotFound,
            Detail = HeadlessProblemDetailsConstants.Details.EndpointNotFound(
                httpContextAccessor.HttpContext?.Request.Path.Value ?? ""
            ),
        };

        _Normalize(problemDetails);

        return problemDetails;
    }

    public ProblemDetails EntityNotFound(string entity, string key)
    {
        _ = entity;
        _ = key;

        var problemDetails = new ProblemDetails
        {
            Status = StatusCodes.Status404NotFound,
            Title = HeadlessProblemDetailsConstants.Titles.EntityNotFound,
            Detail = HeadlessProblemDetailsConstants.Details.EntityNotFound,
        };

        _Normalize(problemDetails);

        return problemDetails;
    }

    public ProblemDetails MalformedSyntax()
    {
        var problemDetails = new ProblemDetails
        {
            Status = StatusCodes.Status400BadRequest,
            Title = HeadlessProblemDetailsConstants.Titles.BadRequest,
            Detail = HeadlessProblemDetailsConstants.Details.BadRequest,
        };

        _Normalize(problemDetails);

        return problemDetails;
    }

    public ProblemDetails UnprocessableEntity(Dictionary<string, List<ErrorDescriptor>> errors)
    {
        var problemDetails = new ProblemDetails
        {
            Title = HeadlessProblemDetailsConstants.Titles.UnprocessableEntity,
            Status = StatusCodes.Status422UnprocessableEntity,
            Detail = HeadlessProblemDetailsConstants.Details.UnprocessableEntity,
            Extensions = { ["errors"] = errors },
        };

        _Normalize(problemDetails);

        return problemDetails;
    }

    public ProblemDetails Conflict(params IEnumerable<ErrorDescriptor> errors)
    {
        var problemDetails = new ProblemDetails
        {
            Status = StatusCodes.Status409Conflict,
            Title = HeadlessProblemDetailsConstants.Titles.Conflict,
            Detail = HeadlessProblemDetailsConstants.Details.Conflict,
            Extensions = { ["errors"] = errors },
        };

        _Normalize(problemDetails);

        return problemDetails;
    }

    public ProblemDetails Unauthorized()
    {
        var problemDetails = new ProblemDetails
        {
            Status = StatusCodes.Status401Unauthorized,
            Title = HeadlessProblemDetailsConstants.Titles.Unauthorized,
            Detail = HeadlessProblemDetailsConstants.Details.Unauthorized,
        };

        _Normalize(problemDetails);

        return problemDetails;
    }

    public ProblemDetails Forbidden(params IReadOnlyCollection<ErrorDescriptor> errors)
    {
        var problemDetails = new ProblemDetails
        {
            Status = StatusCodes.Status403Forbidden,
            Title = HeadlessProblemDetailsConstants.Titles.Forbidden,
            Detail = HeadlessProblemDetailsConstants.Details.Forbidden,
        };

        if (errors.Count > 0)
        {
            problemDetails.Extensions["errors"] = errors;
        }

        _Normalize(problemDetails);

        return problemDetails;
    }

    public void Normalize(ProblemDetails problemDetails)
    {
        Argument.IsNotNull(problemDetails);

        if (
            problemDetails.Status.HasValue
            && apiOptionsAccessor.Value.ClientErrorMapping.TryGetValue(
                problemDetails.Status.Value,
                out var clientErrorData
            )
        )
        {
            problemDetails.Title ??= clientErrorData.Title;
            problemDetails.Type ??= clientErrorData.Link;
        }

        switch (problemDetails.Status)
        {
            case 500:
                problemDetails.Title = HeadlessProblemDetailsConstants.Titles.InternalError;
                problemDetails.Detail ??= HeadlessProblemDetailsConstants.Details.InternalError;

                break;
            case 404
                when !string.Equals(
                    problemDetails.Title,
                    HeadlessProblemDetailsConstants.Titles.EntityNotFound,
                    StringComparison.Ordinal
                ):
                problemDetails.Title = HeadlessProblemDetailsConstants.Titles.EndpointNotFound;

                break;
        }

        if (!problemDetails.Extensions.ContainsKey("traceId"))
        {
            problemDetails.Extensions["traceId"] =
                Activity.Current?.Id ?? httpContextAccessor.HttpContext?.TraceIdentifier;
        }
        if (!problemDetails.Extensions.ContainsKey("buildNumber"))
        {
            problemDetails.Extensions["buildNumber"] = buildInformationAccessor.GetBuildNumber();
        }
        if (!problemDetails.Extensions.ContainsKey("commitNumber"))
        {
            problemDetails.Extensions["commitNumber"] = buildInformationAccessor.GetCommitNumber();
        }
        if (!problemDetails.Extensions.ContainsKey("timestamp"))
        {
            problemDetails.Extensions["timestamp"] = timeProvider.GetUtcNow().ToString("O");
        }
        if (httpContextAccessor.HttpContext is not null)
        {
            problemDetails.Instance = httpContextAccessor.HttpContext.Request.Path.Value ?? "";
        }
    }

    private void _Normalize(ProblemDetails problemDetails)
    {
        Normalize(problemDetails);

        if (httpContextAccessor.HttpContext is not null)
        {
            problemOptionsAccessor?.Value.CustomizeProblemDetails?.Invoke(
                new ProblemDetailsContext
                {
                    HttpContext = httpContextAccessor.HttpContext,
                    ProblemDetails = problemDetails,
                }
            );
        }
    }

    public ProblemDetails TooManyRequests(int retryAfterSeconds)
    {
        var problemDetails = new ProblemDetails
        {
            Status = StatusCodes.Status429TooManyRequests,
            Title = HeadlessProblemDetailsConstants.Titles.TooManyRequests,
            Detail = HeadlessProblemDetailsConstants.Details.TooManyRequests,
            Extensions = { ["retryAfter"] = retryAfterSeconds },
        };

        _Normalize(problemDetails);

        return problemDetails;
    }
}
