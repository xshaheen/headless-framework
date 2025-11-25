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
            Title = ProblemDetailsConstants.Titles.EndpointNotFound,
            Detail = ProblemDetailsConstants.Details.EndpointNotFound(
                httpContextAccessor.HttpContext?.Request.Path.Value ?? ""
            ),
        };

        _Normalize(problemDetails);

        return problemDetails;
    }

    public ProblemDetails EntityNotFound(string entity, string key)
    {
        var problemDetails = new ProblemDetails
        {
            Status = StatusCodes.Status404NotFound,
            Title = ProblemDetailsConstants.Titles.EntityNotFound,
            Detail = ProblemDetailsConstants.Details.EntityNotFound(entity, key),
            Extensions = { ["params"] = new { entity, key } },
        };

        _Normalize(problemDetails);

        return problemDetails;
    }

    public ProblemDetails MalformedSyntax()
    {
        var problemDetails = new ProblemDetails
        {
            Status = StatusCodes.Status400BadRequest,
            Title = ProblemDetailsConstants.Titles.BadRequest,
            Detail = ProblemDetailsConstants.Details.BadRequest,
        };

        _Normalize(problemDetails);

        return problemDetails;
    }

    public ProblemDetails UnprocessableEntity(Dictionary<string, List<ErrorDescriptor>> errors)
    {
        var problemDetails = new ProblemDetails
        {
            Title = ProblemDetailsConstants.Titles.UnprocessableEntity,
            Status = StatusCodes.Status422UnprocessableEntity,
            Detail = ProblemDetailsConstants.Details.UnprocessableEntity,
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
            Title = ProblemDetailsConstants.Titles.Conflict,
            Detail = ProblemDetailsConstants.Details.Conflict,
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
            Title = ProblemDetailsConstants.Titles.Unauthorized,
            Detail = ProblemDetailsConstants.Details.Unauthorized,
        };

        _Normalize(problemDetails);

        return problemDetails;
    }

    public ProblemDetails Forbidden(params IReadOnlyCollection<ErrorDescriptor> errors)
    {
        var problemDetails = new ProblemDetails
        {
            Status = StatusCodes.Status403Forbidden,
            Title = ProblemDetailsConstants.Titles.Forbidden,
            Detail = ProblemDetailsConstants.Details.Forbidden,
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
            Title = ProblemDetailsConstants.Titles.TooManyRequests,
            Detail = ProblemDetailsConstants.Details.TooManyRequests,
        };

        problemDetails.Extensions["retryAfter"] = retryAfterSeconds;

        _Normalize(problemDetails);

        return problemDetails;
    }
}
