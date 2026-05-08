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

/// <summary>
/// Builds normalized <see cref="ProblemDetails"/> for the framework's standard error responses
/// (RFC 7807). Each factory stamps a stable <c>Title</c>/<c>Type</c> from
/// <see cref="HeadlessProblemDetailsConstants"/> and runs the result through <see cref="Normalize"/>,
/// so callers get a consistent wire shape regardless of where the error originated (exception
/// handlers, middleware, endpoint code).
/// </summary>
/// <remarks>
/// Most factories accept an optional <see cref="ErrorDescriptor"/> that is written to
/// <c>Extensions["error"]</c> as a machine-readable discriminator. Clients should branch on that
/// code rather than parse the human-readable <c>Detail</c>.
/// </remarks>
public interface IProblemDetailsCreator
{
    /// <summary>
    /// Builds a normalized 404 <see cref="ProblemDetails"/> for unmatched routes (typically emitted
    /// by <c>StatusCodesRewriterMiddleware</c> when ASP.NET Core's routing produces a bare 404).
    /// The current request path is embedded in <c>Detail</c>.
    /// </summary>
    /// <param name="error">
    /// Optional <see cref="ErrorDescriptor"/> stamped into <c>Extensions["error"]</c>.
    /// </param>
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

    /// <summary>
    /// Builds a normalized 429 <see cref="ProblemDetails"/> for rate-limited responses.
    /// </summary>
    /// <param name="retryAfterSeconds">
    /// Seconds the client should wait before retrying. Written to <c>Extensions["retryAfter"]</c>.
    /// Callers are responsible for setting the matching <c>Retry-After</c> response header.
    /// </param>
    /// <param name="error">
    /// Optional <see cref="ErrorDescriptor"/> stamped into <c>Extensions["error"]</c>.
    /// </param>
    ProblemDetails TooManyRequests(int retryAfterSeconds, ErrorDescriptor? error = null);

    /// <summary>
    /// Builds a normalized 422 <see cref="ProblemDetails"/> for validation failures (typically
    /// mapped from <see cref="FluentValidation.ValidationException"/>).
    /// </summary>
    /// <param name="errors">
    /// Field-keyed map of validation errors written to <c>Extensions["errors"]</c>. Keys are member
    /// paths (e.g., <c>"email"</c>, <c>"address.city"</c>) and values are the descriptors that
    /// failed for that field.
    /// </param>
    ProblemDetails UnprocessableEntity(Dictionary<string, List<ErrorDescriptor>> errors);

    /// <summary>
    /// Builds a normalized 409 <see cref="ProblemDetails"/> for conflicts (typically mapped from
    /// <see cref="Headless.Exceptions.ConflictException"/>, EF concurrency failures, or duplicate
    /// idempotency keys).
    /// </summary>
    /// <param name="errors">
    /// One or more <see cref="ErrorDescriptor"/>s written to <c>Extensions["errors"]</c>. Clients
    /// branch on the descriptor codes to distinguish concurrency failures from domain conflicts.
    /// </param>
    ProblemDetails Conflict(params IReadOnlyCollection<ErrorDescriptor> errors);

    /// <summary>
    /// Builds a normalized 403 <see cref="ProblemDetails"/> for authorization failures (typically
    /// emitted by <c>StatusCodesRewriterMiddleware</c> when ASP.NET Core's authorization pipeline
    /// produces a bare 403).
    /// </summary>
    /// <param name="errors">
    /// Optional <see cref="ErrorDescriptor"/>s written to <c>Extensions["errors"]</c> when the
    /// collection is non-empty. Pass <see langword="null"/> or an empty collection to emit a 403
    /// carrying no machine-readable discriminator (the default for opaque permission denials).
    /// </param>
    ProblemDetails Forbidden(params IReadOnlyCollection<ErrorDescriptor>? errors);

    /// <summary>
    /// Builds a normalized 401 <see cref="ProblemDetails"/> for unauthenticated requests (typically
    /// emitted by <c>StatusCodesRewriterMiddleware</c> when the authentication pipeline produces a
    /// bare 401). Callers are responsible for any <c>WWW-Authenticate</c> response header.
    /// </summary>
    /// <param name="error">
    /// Optional <see cref="ErrorDescriptor"/> stamped into <c>Extensions["error"]</c>.
    /// </param>
    ProblemDetails Unauthorized(ErrorDescriptor? error = null);

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

    /// <summary>
    /// Backfills the framework's standard fields on an externally-produced <see cref="ProblemDetails"/>
    /// so empty-body responses written by upstream middleware (e.g., <c>RequestTimeoutsMiddleware</c>
    /// for 408, anything that just sets a 501 status) match the shape produced by the factories on
    /// this interface.
    /// </summary>
    /// <remarks>
    /// Resolves <c>Title</c>/<c>Type</c> from <see cref="Microsoft.AspNetCore.Mvc.ApiBehaviorOptions.ClientErrorMapping"/>,
    /// then fills missing <c>Title</c>/<c>Type</c>/<c>Detail</c> for status codes the framework
    /// cares about (404, 408, 500, 501) from <see cref="HeadlessProblemDetailsConstants"/>.
    /// Always stamps <c>traceId</c>, <c>buildNumber</c>, <c>commitNumber</c>, and <c>timestamp</c>
    /// extensions, plus <c>Instance</c> from the current request path. Idempotent: existing values
    /// are preserved.
    /// </remarks>
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

    public ProblemDetails Conflict(params IReadOnlyCollection<ErrorDescriptor> errors)
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

    public ProblemDetails Forbidden(params IReadOnlyCollection<ErrorDescriptor>? errors)
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
            problemDetails.Extensions["error"] = error;
        }
    }
}
