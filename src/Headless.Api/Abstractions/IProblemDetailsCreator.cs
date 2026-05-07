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
    ProblemDetails EndpointNotFound();

    ProblemDetails EntityNotFound(string entity, string key);

    ProblemDetails MalformedSyntax();

    ProblemDetails TooManyRequests(int retryAfterSeconds);

    ProblemDetails UnprocessableEntity(Dictionary<string, List<ErrorDescriptor>> errors);

    ProblemDetails Conflict(params IEnumerable<ErrorDescriptor> errors);

    ProblemDetails Unauthorized();

    ProblemDetails Forbidden(params IReadOnlyCollection<ErrorDescriptor> errors);

    /// <summary>
    /// Builds a normalized 400 <see cref="ProblemDetails"/> for the cross-layer
    /// "missing tenant context" guard (see <c>Headless.Abstractions.MissingTenantContextException</c>).
    /// </summary>
    /// <returns>
    /// A <see cref="ProblemDetails"/> already passed through <see cref="Normalize"/> — callers should
    /// not call <see cref="Normalize"/> again. Contains <c>Status = 400</c>, the standard
    /// <c>bad-request</c> title (so clients see this as a regular 400), a tenancy-specific detail
    /// message, the standard 400 <c>type</c> URL (filled by <see cref="Normalize"/> from
    /// <see cref="ApiBehaviorOptions.ClientErrorMapping"/>), and the stable <c>code</c> extension
    /// (<c>HeadlessProblemDetailsConstants.Codes.TenantContextRequired</c>) — the only field that
    /// distinguishes this from any other 400. Deliberately surfaces no entity name, exception
    /// message, or layer tag — those belong in server logs, not the HTTP response.
    /// </returns>
    /// <remarks>
    /// This is the canonical factory for the tenancy 400 response. The framework's
    /// <c>HeadlessApiExceptionHandler</c> delegates to this method; direct callers (e.g., a
    /// pre-handler that wants to short-circuit a request) should also prefer it over hand-building
    /// a <see cref="ProblemDetails"/>. Requires the host to have called
    /// <c>services.AddHeadlessProblemDetails()</c> so the underlying creator can run normalization.
    /// </remarks>
    ProblemDetails TenantRequired();

    /// <summary>
    /// Builds a normalized 408 <see cref="ProblemDetails"/> for request-timeout responses
    /// (typically mapped from <see cref="System.TimeoutException"/>).
    /// </summary>
    /// <returns>
    /// A <see cref="ProblemDetails"/> already passed through <see cref="Normalize"/> — callers should
    /// not call <see cref="Normalize"/> again. Contains <c>Status = 408</c>, the standard
    /// <c>request-timeout</c> title and detail, and the standard normalized extensions.
    /// Deliberately surfaces no exception message or inner-exception content — those belong in
    /// server logs, not the HTTP response.
    /// </returns>
    /// <remarks>
    /// Requires the host to have called <c>services.AddHeadlessProblemDetails()</c> so the
    /// underlying creator can run normalization.
    /// </remarks>
    ProblemDetails RequestTimeout();

    /// <summary>
    /// Builds a normalized 501 <see cref="ProblemDetails"/> for unimplemented-functionality
    /// responses (typically mapped from <see cref="System.NotImplementedException"/>).
    /// </summary>
    /// <returns>
    /// A <see cref="ProblemDetails"/> already passed through <see cref="Normalize"/> — callers should
    /// not call <see cref="Normalize"/> again. Contains <c>Status = 501</c>, the standard
    /// <c>not-implemented</c> title and detail, and the standard normalized extensions.
    /// Deliberately surfaces no exception message or inner-exception content — those belong in
    /// server logs, not the HTTP response.
    /// </returns>
    /// <remarks>
    /// Requires the host to have called <c>services.AddHeadlessProblemDetails()</c> so the
    /// underlying creator can run normalization.
    /// </remarks>
    ProblemDetails NotImplemented();

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

    public ProblemDetails TenantRequired()
    {
        var problemDetails = new ProblemDetails
        {
            Status = StatusCodes.Status400BadRequest,
            Title = HeadlessProblemDetailsConstants.Titles.BadRequest,
            Detail = HeadlessProblemDetailsConstants.Details.TenantContextRequired,
            Extensions = { ["code"] = HeadlessProblemDetailsConstants.Codes.TenantContextRequired },
        };

        _Normalize(problemDetails);

        return problemDetails;
    }

    public ProblemDetails RequestTimeout()
    {
        var problemDetails = new ProblemDetails
        {
            Status = StatusCodes.Status408RequestTimeout,
            Title = HeadlessProblemDetailsConstants.Titles.RequestTimeout,
            Detail = HeadlessProblemDetailsConstants.Details.RequestTimeout,
        };

        _Normalize(problemDetails);

        return problemDetails;
    }

    public ProblemDetails NotImplemented()
    {
        var problemDetails = new ProblemDetails
        {
            Status = StatusCodes.Status501NotImplemented,
            Title = HeadlessProblemDetailsConstants.Titles.NotImplemented,
            Detail = HeadlessProblemDetailsConstants.Details.NotImplemented,
        };

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
