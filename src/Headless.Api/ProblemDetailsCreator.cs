// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Diagnostics;
using Headless.Api.Resources;
using Headless.Checks;
using Headless.Context;
using Headless.Primitives;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace Headless.Api;

internal sealed class ProblemDetailsCreator(
    TimeProvider timeProvider,
    IBuildInformationAccessor buildInformationAccessor,
    IHttpContextAccessor httpContextAccessor,
    IOptions<ApiBehaviorOptions> apiOptionsAccessor
) : IProblemDetailsCreator
{
    public ProblemDetails EndpointNotFound(string? detail = null, ErrorDescriptor? error = null)
    {
        var problemDetails = new ProblemDetails
        {
            Status = StatusCodes.Status404NotFound,
            Title = HeadlessProblemDetailsConstants.Titles.EndpointNotFound,
            Detail = detail ?? _EndpointNotFoundDetail(),
        };

        _SetError(problemDetails, error);
        _Normalize(problemDetails);

        return problemDetails;
    }

    public ProblemDetails EntityNotFound(string? detail = null, ErrorDescriptor? error = null)
    {
        var problemDetails = new ProblemDetails
        {
            Status = StatusCodes.Status404NotFound,
            Title = HeadlessProblemDetailsConstants.Titles.EntityNotFound,
            Detail = detail ?? Messages.problem_entity_not_found,
        };

        _SetError(problemDetails, error);
        _Normalize(problemDetails);

        return problemDetails;
    }

    public ProblemDetails BadRequest(string? detail = null, ErrorDescriptor? error = null)
    {
        var problemDetails = new ProblemDetails
        {
            Status = StatusCodes.Status400BadRequest,
            Title = HeadlessProblemDetailsConstants.Titles.BadRequest,
            Detail = detail ?? Messages.problem_bad_request,
        };

        _SetError(problemDetails, error);
        _Normalize(problemDetails);

        return problemDetails;
    }

    public ProblemDetails UnprocessableEntity(IReadOnlyDictionary<string, IReadOnlyList<ErrorDescriptor>> errors)
    {
        var problemDetails = new ProblemDetails
        {
            Title = HeadlessProblemDetailsConstants.Titles.UnprocessableEntity,
            Status = StatusCodes.Status422UnprocessableEntity,
            Detail = Messages.problem_unprocessable_entity,
            Extensions = { ["errors"] = errors },
        };

        _Normalize(problemDetails);

        return problemDetails;
    }

    public ProblemDetails Conflict(params IReadOnlyCollection<ErrorDescriptor> errors)
    {
        var problemDetails = new ProblemDetails
        {
            Status = StatusCodes.Status409Conflict,
            Title = HeadlessProblemDetailsConstants.Titles.Conflict,
            Detail = Messages.problem_conflict,
            Extensions = { ["errors"] = errors },
        };

        _Normalize(problemDetails);

        return problemDetails;
    }

    public ProblemDetails Forbidden(string? detail = null, ErrorDescriptor? error = null)
    {
        var problemDetails = new ProblemDetails
        {
            Status = StatusCodes.Status403Forbidden,
            Title = HeadlessProblemDetailsConstants.Titles.Forbidden,
            Detail = detail ?? Messages.problem_forbidden,
        };

        _SetError(problemDetails, error);
        _Normalize(problemDetails);

        return problemDetails;
    }

    public ProblemDetails Unauthorized(string? detail = null, ErrorDescriptor? error = null)
    {
        var problemDetails = new ProblemDetails
        {
            Status = StatusCodes.Status401Unauthorized,
            Title = HeadlessProblemDetailsConstants.Titles.Unauthorized,
            Detail = detail ?? Messages.problem_unauthorized,
        };

        _SetError(problemDetails, error);
        _Normalize(problemDetails);

        return problemDetails;
    }

    public ProblemDetails RequestTimeout(string? detail = null, ErrorDescriptor? error = null)
    {
        var problemDetails = new ProblemDetails
        {
            Status = StatusCodes.Status408RequestTimeout,
            Title = HeadlessProblemDetailsConstants.Titles.RequestTimeout,
            Detail = detail ?? Messages.problem_request_timeout,
        };

        _SetError(problemDetails, error);
        _Normalize(problemDetails);

        return problemDetails;
    }

    public ProblemDetails NotImplemented(string? detail = null, ErrorDescriptor? error = null)
    {
        var problemDetails = new ProblemDetails
        {
            Status = StatusCodes.Status501NotImplemented,
            Title = HeadlessProblemDetailsConstants.Titles.NotImplemented,
            Detail = detail ?? Messages.problem_not_implemented,
        };

        _SetError(problemDetails, error);
        _Normalize(problemDetails);

        return problemDetails;
    }

    public ProblemDetails TooManyRequests(int retryAfterSeconds, string? detail = null, ErrorDescriptor? error = null)
    {
        var problemDetails = new ProblemDetails
        {
            Status = StatusCodes.Status429TooManyRequests,
            Title = HeadlessProblemDetailsConstants.Titles.TooManyRequests,
            Detail = detail ?? Messages.problem_too_many_requests,
            Extensions = { ["retryAfter"] = retryAfterSeconds },
        };

        _SetError(problemDetails, error);
        _Normalize(problemDetails);

        return problemDetails;
    }

    public ProblemDetails ServiceUnavailable(
        int? retryAfterSeconds = null,
        string? detail = null,
        ErrorDescriptor? error = null
    )
    {
        var problemDetails = new ProblemDetails
        {
            Status = StatusCodes.Status503ServiceUnavailable,
            Title = HeadlessProblemDetailsConstants.Titles.ServiceUnavailable,
            Detail = detail ?? Messages.problem_service_unavailable,
        };

        // Only stamped when the caller knows a duration. An invented retryAfter would have clients
        // synchronize their retries on a value the server never promised.
        if (retryAfterSeconds.HasValue)
        {
            problemDetails.Extensions["retryAfter"] = retryAfterSeconds.Value;
        }

        _SetError(problemDetails, error);
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
                problemDetails.Detail ??= Messages.problem_internal_error;

                break;
            case 404
                when !string.Equals(
                    problemDetails.Title,
                    HeadlessProblemDetailsConstants.Titles.EntityNotFound,
                    StringComparison.Ordinal
                ):
                problemDetails.Title = HeadlessProblemDetailsConstants.Titles.EndpointNotFound;
                problemDetails.Detail ??= _EndpointNotFoundDetail();

                break;
            // 408, 413, and 501 are not in ASP.NET Core's default ApiBehaviorOptions.ClientErrorMapping,
            // so the lookup above leaves Title and Type null. Backfill from the framework's own
            // constants here — same path as 500/404 — so empty-body responses written by
            // RequestTimeoutsMiddleware (408), IdempotencyMiddleware oversize (413), or any middleware
            // that just sets the status code (501) produce a consistent shape. Detail is also filled,
            // which ClientErrorMapping cannot carry.
            case 408:
                problemDetails.Title ??= HeadlessProblemDetailsConstants.Titles.RequestTimeout;
                problemDetails.Type ??= HeadlessProblemDetailsConstants.Types.RequestTimeout;
                problemDetails.Detail ??= Messages.problem_request_timeout;

                break;
            case 413:
                problemDetails.Title ??= HeadlessProblemDetailsConstants.Titles.PayloadTooLarge;
                problemDetails.Type ??= HeadlessProblemDetailsConstants.Types.PayloadTooLarge;
                problemDetails.Detail ??= Messages.problem_payload_too_large;

                break;
            case 501:
                problemDetails.Title ??= HeadlessProblemDetailsConstants.Titles.NotImplemented;
                problemDetails.Type ??= HeadlessProblemDetailsConstants.Types.NotImplemented;
                problemDetails.Detail ??= Messages.problem_not_implemented;

                break;
        }

        if (!problemDetails.Extensions.ContainsKey("traceId"))
        {
            problemDetails.Extensions["traceId"] =
                Activity.Current?.Id ?? httpContextAccessor.HttpContext?.TraceIdentifier;
        }

        if (!problemDetails.Extensions.ContainsKey("buildNumber"))
        {
            problemDetails.Extensions["buildNumber"] = buildInformationAccessor.GetVersion();
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
    }

#pragma warning disable CA1863 // Use 'CompositeFormat': the format is a resource that changes with the UI culture, so a cached CompositeFormat would pin one language.
    private string _EndpointNotFoundDetail()
    {
        return string.Format(
            CultureInfo.CurrentCulture,
            Messages.problem_endpoint_not_found,
            httpContextAccessor.HttpContext?.Request.Path.Value ?? ""
        );
    }
#pragma warning restore CA1863

    private static void _SetError(ProblemDetails problemDetails, ErrorDescriptor? error)
    {
        if (error is not null)
        {
            // Project to a minimal { code, description } shape — Severity and Params are
            // server-side metadata that don't belong on the wire for the single-error discriminator.
            problemDetails.Extensions["error"] = error;
        }
    }
}
