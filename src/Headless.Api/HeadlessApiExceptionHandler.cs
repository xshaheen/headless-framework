// Copyright (c) Mahmoud Shaheen. All rights reserved.

using FluentValidation;
using Headless.Abstractions;
using Headless.Api.Abstractions;
using Headless.Api.Resources;
using Headless.Constants;
using Headless.Exceptions;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using JsonOptions = Microsoft.AspNetCore.Http.Json.JsonOptions;

namespace Headless.Api;

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
    private const string _DbUpdateConcurrencyExceptionTypeName = "DbUpdateConcurrencyException";

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
            // Cancellation handled first (covers OCE at any nesting depth from Task.WhenAll etc.).
            case Exception when _IsCancellationException(exception):
                // Client closed the request — status only, no body. The client is gone.
                if (httpContext.Response.HasStarted)
                {
                    return false;
                }
                httpContext.Response.StatusCode = StatusCodes.Status499ClientClosedRequest;
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

            // EF Core's DbUpdateConcurrencyException matched by simple type name to avoid a hard
            // EF Core dependency in Headless.Api. Known caveat: any unrelated user-defined
            // exception coincidentally named "DbUpdateConcurrencyException" in another namespace
            // will be mapped to 409 here. We accept this trade-off because the alternative
            // (FullName match against "Microsoft.EntityFrameworkCore.DbUpdateConcurrencyException")
            // would silently miss future EF Core type renames.
            case Exception
                when string.Equals(
                    exception.GetType().Name,
                    _DbUpdateConcurrencyExceptionTypeName,
                    StringComparison.Ordinal
                ):
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

    private static bool _IsCancellationException(Exception ex)
    {
        for (var current = ex; current is not null; current = current.InnerException)
        {
            if (current is OperationCanceledException)
            {
                return true;
            }
        }
        return false;
    }

    private static bool _AcceptsJsonProblemDetails(HttpRequest request)
    {
        return request.CanAccept(ContentTypes.Applications.Json, ContentTypes.Applications.ProblemJson);
    }

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
