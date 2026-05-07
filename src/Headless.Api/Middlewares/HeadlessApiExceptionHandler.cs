// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Diagnostics;
using FluentValidation;
using Headless.Abstractions;
using Headless.Api.Abstractions;
using Headless.Api.Resources;
using Headless.Constants;
using Headless.Exceptions;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using JsonOptions = Microsoft.AspNetCore.Http.Json.JsonOptions;

namespace Headless.Api.Middlewares;

/// <summary>
/// Maps framework-known exceptions to normalized ProblemDetails responses through ASP.NET Core's
/// <see cref="IExceptionHandler"/> pipeline. Replaces the per-package <c>MvcApiExceptionFilter</c>
/// and <c>MinimalApiExceptionFilter</c>.
/// </summary>
/// <remarks>
/// <para>
/// Covers any unhandled exception that bubbles to ASP.NET Core's exception-handler middleware —
/// typically MVC actions and Minimal-API endpoints. Middleware running before
/// <c>UseExceptionHandler</c>, hosted/background services, and SignalR hubs need their own catch
/// sites.
/// </para>
/// <para>
/// Information-disclosure invariant: response bodies surface only the framework-owned fields
/// produced by <see cref="IProblemDetailsCreator"/> plus the standard normalized extensions.
/// Exception messages, <see cref="System.Exception.Data"/>, and inner-exception content are NOT
/// surfaced — they belong in server logs.
/// </para>
/// </remarks>
internal sealed partial class HeadlessApiExceptionHandler(
    IOptions<JsonOptions> jsonOptions,
    IProblemDetailsService problemDetailsService,
    IProblemDetailsCreator problemDetailsCreator,
    ILogger<HeadlessApiExceptionHandler> logger
) : IExceptionHandler
{
    private const string _DbUpdateConcurrencyExceptionFullName =
        "Microsoft.EntityFrameworkCore.DbUpdateConcurrencyException";

    public async ValueTask<bool> TryHandleAsync(
        HttpContext httpContext,
        Exception exception,
        CancellationToken cancellationToken
    )
    {
        ProblemDetails? problemDetails;
        int statusCode;

        switch (exception)
        {
            // Cancellation handled first. Only treat OCE as client-cancelled when RequestAborted
            // signaled (matches the contract a per-pipeline RequestCanceled middleware would have
            // applied). Server-side cancellations and library-thrown OCE fall through to default.
            case not null when _IsCancellationException(exception):
                if (!httpContext.RequestAborted.IsCancellationRequested)
                {
                    return false;
                }
                if (httpContext.Response.HasStarted)
                {
                    return false;
                }
                httpContext.Response.StatusCode = StatusCodes.Status499ClientClosedRequest;
                httpContext
                    .Features.Get<IHttpActivityFeature>()
                    ?.Activity?.AddEvent(new ActivityEvent("Client cancelled the request"));
                _LogRequestCanceled(logger);
                return true;

            case MissingTenantContextException:
                problemDetails = problemDetailsCreator.TenantRequired();
                statusCode = StatusCodes.Status400BadRequest;
                break;

            case ConflictException conflict:
                problemDetails = problemDetailsCreator.Conflict(conflict.Errors);
                statusCode = StatusCodes.Status409Conflict;
                break;

            case ValidationException validation:
                problemDetails = problemDetailsCreator.UnprocessableEntity(validation.Errors.ToErrorDescriptors());
                statusCode = StatusCodes.Status422UnprocessableEntity;
                break;

            case EntityNotFoundException notFound:
                problemDetails = problemDetailsCreator.EntityNotFound(notFound.Entity, notFound.Key);
                statusCode = StatusCodes.Status404NotFound;
                break;

            // EF Core's DbUpdateConcurrencyException matched by full type name (walking the
            // inheritance chain) to avoid a hard EF Core dependency in Headless.Api while
            // accepting subclasses defined by consumers. Trade-offs vs. simple-name match:
            // false positives from unrelated user types named DbUpdateConcurrencyException
            // are eliminated; future EF Core type renames (rare; would be caught by tests) will
            // silently stop being mapped — accepted because consumer namespace collisions are
            // the more common real-world risk.
            case not null when _IsDbUpdateConcurrencyException(exception):
                _LogDbConcurrencyException(logger, exception);
                problemDetails = problemDetailsCreator.Conflict([GeneralMessageDescriber.ConcurrencyFailure()]);
                statusCode = StatusCodes.Status409Conflict;
                break;

            case TimeoutException:
                _LogRequestTimeoutException(logger, exception);
                problemDetails = problemDetailsCreator.RequestTimeout();
                statusCode = StatusCodes.Status408RequestTimeout;
                break;

            case NotImplementedException:
                problemDetails = problemDetailsCreator.NotImplemented();
                statusCode = StatusCodes.Status501NotImplemented;
                break;

            default:
                return false;
        }

        // Guard before mutating the response: setting StatusCode after the response has started
        // throws on Kestrel. Mirrors the OCE branch's HasStarted check for partial-write safety.
        if (httpContext.Response.HasStarted)
        {
            _LogResponseAlreadyStarted(logger, exception.GetType().Name);
            return false;
        }

        httpContext.Response.StatusCode = statusCode;

        var written = await problemDetailsService
            .TryWriteAsync(
                new ProblemDetailsContext
                {
                    HttpContext = httpContext,
                    Exception = exception,
                    ProblemDetails = problemDetails,
                }
            )
            .ConfigureAwait(false);

        if (written)
        {
            return true;
        }

        if (httpContext.Response.HasStarted)
        {
            _LogResponseAlreadyStarted(logger, exception.GetType().Name);
            return false;
        }

        // Accept-header gate: only emit the JSON-shaped fallback when the client actually accepts
        // JSON. Otherwise let the platform render its default response (mirrors the gate that the
        // deleted MvcApiExceptionFilter / MinimalApiExceptionFilter applied).
        if (!_AcceptsJsonProblemDetails(httpContext.Request))
        {
            return false;
        }

        try
        {
            await httpContext
                .Response.WriteAsJsonAsync(
                    problemDetails,
                    jsonOptions.Value.SerializerOptions,
                    contentType: "application/problem+json",
                    cancellationToken: cancellationToken
                )
                .ConfigureAwait(false);
            return true;
        }
        catch (OperationCanceledException)
        {
            // Client cancelled while we were writing the fallback body — nothing else we can do.
            return false;
        }
#pragma warning disable CA1031 // Do not catch general exception types — last-resort fallback path; we must not re-throw.
        catch (Exception fallbackError)
#pragma warning restore CA1031
        {
            _LogFallbackWriteFailed(logger, fallbackError, fallbackError.GetType().Name);
            return false;
        }
    }

    private static bool _IsDbUpdateConcurrencyException(Exception ex)
    {
        for (var type = ex.GetType(); type is not null && type != typeof(Exception); type = type.BaseType)
        {
            if (string.Equals(type.FullName, _DbUpdateConcurrencyExceptionFullName, StringComparison.Ordinal))
            {
                return true;
            }
        }
        return false;
    }

    private static bool _IsCancellationException(Exception? ex)
    {
        if (ex is null)
        {
            return false;
        }

        if (ex is OperationCanceledException)
        {
            return true;
        }

        if (ex is AggregateException aggregate)
        {
            foreach (var inner in aggregate.InnerExceptions)
            {
                if (_IsCancellationException(inner))
                {
                    return true;
                }
            }
            return false;
        }

        return _IsCancellationException(ex.InnerException);
    }

    private static bool _AcceptsJsonProblemDetails(HttpRequest request)
    {
        return request.CanAccept(ContentTypes.Applications.Json, ContentTypes.Applications.ProblemJson);
    }

    [LoggerMessage(
        EventId = 5002,
        EventName = "RequestCancelled",
        Level = LogLevel.Information,
        Message = "Client cancelled the request"
    )]
    private static partial void _LogRequestCanceled(ILogger logger);

    [LoggerMessage(
        EventId = 5003,
        EventName = "DbConcurrencyException",
        Level = LogLevel.Warning,
        Message = "Database concurrency exception occurred",
        SkipEnabledCheck = true
    )]
    private static partial void _LogDbConcurrencyException(ILogger logger, Exception exception);

    [LoggerMessage(
        EventId = 5004,
        EventName = "RequestTimeoutException",
        Level = LogLevel.Debug,
        Message = "Request was timed out",
        SkipEnabledCheck = true
    )]
    private static partial void _LogRequestTimeoutException(ILogger logger, Exception exception);

    [LoggerMessage(
        EventId = 5006,
        EventName = "ResponseAlreadyStarted",
        Level = LogLevel.Error,
        Message = "Cannot map {ExceptionType}: response has already started; client receives the partial response",
        SkipEnabledCheck = true
    )]
    private static partial void _LogResponseAlreadyStarted(ILogger logger, string exceptionType);

    [LoggerMessage(
        EventId = 5007,
        EventName = "FallbackWriteFailed",
        Level = LogLevel.Error,
        Message = "Failed to write fallback ProblemDetails response ({FallbackErrorType}); pipeline will produce the default response",
        SkipEnabledCheck = true
    )]
    private static partial void _LogFallbackWriteFailed(ILogger logger, Exception exception, string fallbackErrorType);
}
