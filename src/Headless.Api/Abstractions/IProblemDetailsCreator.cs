// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Diagnostics;
using Headless.Abstractions;
using Headless.Checks;
using Headless.Constants;
using Headless.Primitives;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace Headless.Api.Abstractions;

public interface IProblemDetailsCreator
{
    ProblemDetails EndpointNotFound(ErrorDescriptor? error = null);

    /// <summary>
    /// Builds a normalized 404 <see cref="ProblemDetails"/> for entity-not-found responses
    /// (typically mapped from <see cref="Headless.Exceptions.EntityNotFoundException"/>).
    /// </summary>
    /// <param name="error">
    /// Optional <see cref="ErrorDescriptor"/> stamped into <c>Extensions["error"]</c>. Omit to
    /// emit a 404 carrying no machine-readable discriminator.
    /// </param>
    /// <returns>
    /// A <see cref="ProblemDetails"/> already passed through <see cref="Normalize"/>. Deliberately
    /// surfaces no entity name or key — those belong in server logs, not the HTTP response.
    /// </returns>
    ProblemDetails EntityNotFound(ErrorDescriptor? error = null);

    /// <summary>
    /// Builds a normalized 400 <see cref="ProblemDetails"/>. Callers attach a stable
    /// <see cref="ErrorDescriptor"/> to discriminate specific 400 cases (e.g., the cross-layer
    /// "missing tenant context" guard uses
    /// <c>HeadlessProblemDetailsConstants.Errors.TenantContextRequired</c>).
    /// </summary>
    /// <param name="detail">
    /// Optional detail message. Defaults to the framework's generic malformed-syntax message
    /// when <see langword="null"/>.
    /// </param>
    /// <param name="error">
    /// Optional <see cref="ErrorDescriptor"/> written to <c>Extensions["error"]</c>. Clients branch
    /// on this to handle specific 400 cases without relying on detail text.
    /// </param>
    ProblemDetails BadRequest(string? detail = null, ErrorDescriptor? error = null);

    ProblemDetails TooManyRequests(int retryAfterSeconds, ErrorDescriptor? error = null);

    ProblemDetails UnprocessableEntity(Dictionary<string, List<ErrorDescriptor>> errors, ErrorDescriptor? error = null);

    ProblemDetails Conflict(IEnumerable<ErrorDescriptor> errors, ErrorDescriptor? error = null);

    ProblemDetails Unauthorized(ErrorDescriptor? error = null);

    ProblemDetails Forbidden(IReadOnlyCollection<ErrorDescriptor>? errors = null, ErrorDescriptor? error = null);

    /// <summary>
    /// Builds a normalized 408 <see cref="ProblemDetails"/> for request-timeout responses
    /// (typically mapped from <see cref="System.TimeoutException"/>).
    /// </summary>
    ProblemDetails RequestTimeout(ErrorDescriptor? error = null);

    /// <summary>
    /// Builds a normalized 501 <see cref="ProblemDetails"/> for unimplemented-functionality
    /// responses (typically mapped from <see cref="System.NotImplementedException"/>).
    /// </summary>
    ProblemDetails NotImplemented(ErrorDescriptor? error = null);

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
    public ProblemDetails EndpointNotFound(ErrorDescriptor? error = null)
    {
        var problemDetails = new ProblemDetails
        {
            Status = StatusCodes.Status404NotFound,
            Title = HeadlessProblemDetailsConstants.Titles.EndpointNotFound,
            Detail = HeadlessProblemDetailsConstants.Details.EndpointNotFound(
                httpContextAccessor.HttpContext?.Request.Path.Value ?? ""
            ),
        };

        _SetError(problemDetails, error);
        _Normalize(problemDetails);

        return problemDetails;
    }

    public ProblemDetails EntityNotFound(ErrorDescriptor? error = null)
    {
        var problemDetails = new ProblemDetails
        {
            Status = StatusCodes.Status404NotFound,
            Title = HeadlessProblemDetailsConstants.Titles.EntityNotFound,
            Detail = HeadlessProblemDetailsConstants.Details.EntityNotFound,
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
            Detail = detail ?? HeadlessProblemDetailsConstants.Details.BadRequest,
        };

        _SetError(problemDetails, error);
        _Normalize(problemDetails);

        return problemDetails;
    }

    public ProblemDetails UnprocessableEntity(
        Dictionary<string, List<ErrorDescriptor>> errors,
        ErrorDescriptor? error = null
    )
    {
        var problemDetails = new ProblemDetails
        {
            Title = HeadlessProblemDetailsConstants.Titles.UnprocessableEntity,
            Status = StatusCodes.Status422UnprocessableEntity,
            Detail = HeadlessProblemDetailsConstants.Details.UnprocessableEntity,
            Extensions = { ["errors"] = errors },
        };

        _SetError(problemDetails, error);
        _Normalize(problemDetails);

        return problemDetails;
    }

    public ProblemDetails Conflict(IEnumerable<ErrorDescriptor> errors, ErrorDescriptor? error = null)
    {
        var problemDetails = new ProblemDetails
        {
            Status = StatusCodes.Status409Conflict,
            Title = HeadlessProblemDetailsConstants.Titles.Conflict,
            Detail = HeadlessProblemDetailsConstants.Details.Conflict,
            Extensions = { ["errors"] = errors },
        };

        _SetError(problemDetails, error);
        _Normalize(problemDetails);

        return problemDetails;
    }

    public ProblemDetails Unauthorized(ErrorDescriptor? error = null)
    {
        var problemDetails = new ProblemDetails
        {
            Status = StatusCodes.Status401Unauthorized,
            Title = HeadlessProblemDetailsConstants.Titles.Unauthorized,
            Detail = HeadlessProblemDetailsConstants.Details.Unauthorized,
        };

        _SetError(problemDetails, error);
        _Normalize(problemDetails);

        return problemDetails;
    }

    public ProblemDetails Forbidden(IReadOnlyCollection<ErrorDescriptor>? errors = null, ErrorDescriptor? error = null)
    {
        var problemDetails = new ProblemDetails
        {
            Status = StatusCodes.Status403Forbidden,
            Title = HeadlessProblemDetailsConstants.Titles.Forbidden,
            Detail = HeadlessProblemDetailsConstants.Details.Forbidden,
        };

        if (errors is { Count: > 0 })
        {
            problemDetails.Extensions["errors"] = errors;
        }

        _SetError(problemDetails, error);
        _Normalize(problemDetails);

        return problemDetails;
    }

    public ProblemDetails RequestTimeout(ErrorDescriptor? error = null)
    {
        var problemDetails = new ProblemDetails
        {
            Status = StatusCodes.Status408RequestTimeout,
            Title = HeadlessProblemDetailsConstants.Titles.RequestTimeout,
            Detail = HeadlessProblemDetailsConstants.Details.RequestTimeout,
        };

        _SetError(problemDetails, error);
        _Normalize(problemDetails);

        return problemDetails;
    }

    public ProblemDetails NotImplemented(ErrorDescriptor? error = null)
    {
        var problemDetails = new ProblemDetails
        {
            Status = StatusCodes.Status501NotImplemented,
            Title = HeadlessProblemDetailsConstants.Titles.NotImplemented,
            Detail = HeadlessProblemDetailsConstants.Details.NotImplemented,
        };

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
                problemDetails.Detail ??= HeadlessProblemDetailsConstants.Details.InternalError;

                break;
            case 404
                when !string.Equals(
                    problemDetails.Title,
                    HeadlessProblemDetailsConstants.Titles.EntityNotFound,
                    StringComparison.Ordinal
                ):
                problemDetails.Title = HeadlessProblemDetailsConstants.Titles.EndpointNotFound;
                problemDetails.Detail ??= HeadlessProblemDetailsConstants.Details.EndpointNotFound(
                    httpContextAccessor.HttpContext?.Request.Path.Value ?? ""
                );

                break;
            // 408 and 501 are not in ASP.NET Core's default ApiBehaviorOptions.ClientErrorMapping,
            // so the lookup above leaves Title and Type null. Backfill from the framework's own
            // constants here — same path as 500/404 — so empty-body responses written by
            // RequestTimeoutsMiddleware (408) or any middleware that just sets the status code (501)
            // produce the same shape as the IProblemDetailsCreator.RequestTimeout()/NotImplemented()
            // factories. Detail is also filled, which ClientErrorMapping cannot carry.
            case 408:
                problemDetails.Title ??= HeadlessProblemDetailsConstants.Titles.RequestTimeout;
                problemDetails.Type ??= HeadlessProblemDetailsConstants.Types.RequestTimeout;
                problemDetails.Detail ??= HeadlessProblemDetailsConstants.Details.RequestTimeout;

                break;
            case 501:
                problemDetails.Title ??= HeadlessProblemDetailsConstants.Titles.NotImplemented;
                problemDetails.Type ??= HeadlessProblemDetailsConstants.Types.NotImplemented;
                problemDetails.Detail ??= HeadlessProblemDetailsConstants.Details.NotImplemented;

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

    private static void _SetError(ProblemDetails problemDetails, ErrorDescriptor? error)
    {
        if (error is not null)
        {
            // Project to a minimal { code, description } shape — Severity and Params are
            // server-side metadata that don't belong on the wire for the single-error discriminator.
            problemDetails.Extensions["error"] = new ProblemErrorInfo(error.Code, error.Description);
        }
    }

    public ProblemDetails TooManyRequests(int retryAfterSeconds, ErrorDescriptor? error = null)
    {
        var problemDetails = new ProblemDetails
        {
            Status = StatusCodes.Status429TooManyRequests,
            Title = HeadlessProblemDetailsConstants.Titles.TooManyRequests,
            Detail = HeadlessProblemDetailsConstants.Details.TooManyRequests,
            Extensions = { ["retryAfter"] = retryAfterSeconds },
        };

        _SetError(problemDetails, error);
        _Normalize(problemDetails);

        return problemDetails;
    }
}

internal sealed record ProblemErrorInfo(string Code, string Description);
