// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Diagnostics;
using System.Runtime.CompilerServices;
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
/// Exception messages, <see cref="Exception.Data"/>, and inner-exception content are NOT
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

    // Cached per concrete exception type to avoid re-walking the inheritance chain on every hit.
    // ConditionalWeakTable lets entries (and their owning AssemblyLoadContext) unload when the
    // exception type is no longer referenced elsewhere.
    private static readonly ConditionalWeakTable<Type, StrongBox<bool>> _DbUpdateConcurrencyTypeCache = new();

    private static readonly ConditionalWeakTable<
        Type,
        StrongBox<bool>
    >.CreateValueCallback _DbUpdateConcurrencyFactory = static type =>
        _MatchesExceptionFullName(type, _DbUpdateConcurrencyExceptionFullName);

    private static StrongBox<bool> _MatchesExceptionFullName(Type type, string fullName)
    {
        for (var t = type; t is not null && t != typeof(Exception); t = t.BaseType)
        {
            if (string.Equals(t.FullName, fullName, StringComparison.Ordinal))
            {
                return new StrongBox<bool>(value: true);
            }
        }

        return new StrongBox<bool>(value: false);
    }

    public async ValueTask<bool> TryHandleAsync(
        HttpContext httpContext,
        Exception exception,
        CancellationToken cancellationToken
    )
    {
        ProblemDetails? problemDetails;
        int statusCode;

        try
        {
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
                        _LogResponseAlreadyStarted(logger, exception.GetType().Name);
                        return false;
                    }
                    httpContext.Response.StatusCode = StatusCodes.Status499ClientClosedRequest;
                    httpContext
                        .Features.Get<IHttpActivityFeature>()
                        ?.Activity?.AddEvent(new ActivityEvent("Client cancelled the request"));
                    _LogRequestCanceled(logger);
                    return true;

                case MissingTenantContextException missingTenant:
                    _LogMissingTenantContext(
                        logger,
                        missingTenant,
                        missingTenant.GetType().Name,
                        httpContext.Request.Path
                    );
                    if (httpContext.Features.Get<HeadlessTenancyResolutionApplied>() is null)
                    {
                        _LogTenantResolutionMiddlewareMissing(logger, httpContext.Request.Path);
                    }
                    problemDetails = problemDetailsCreator.BadRequest(
                        detail: HeadlessProblemDetailsConstants.Details.TenantContextRequired,
                        error: HeadlessProblemDetailsConstants.Errors.TenantContextRequired
                    );
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

                case EntityNotFoundException:
                    problemDetails = problemDetailsCreator.EntityNotFound();
                    statusCode = StatusCodes.Status404NotFound;
                    break;

                case CrossTenantWriteException:
                    _LogCrossTenantWriteException(logger, exception);
                    problemDetails = problemDetailsCreator.Conflict([
                        HeadlessProblemDetailsConstants.Errors.CrossTenantWrite,
                    ]);
                    statusCode = StatusCodes.Status409Conflict;
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
        }
        catch (OperationCanceledException)
        {
            // Cancellation has its own handling earlier in the switch — let it propagate.
            throw;
        }
#pragma warning disable CA1031 // Last-resort fallback path; creator failures must not re-throw out of the handler.
        catch (Exception creatorError)
#pragma warning restore CA1031
        {
            _LogCreatorFailed(logger, creatorError, exception.GetType().Name);
            return false;
        }

        // Accept-header gate (mirrors the deleted MvcApiExceptionFilter / MinimalApiExceptionFilter
        // gate): only emit a ProblemDetails JSON response when the client actually accepts JSON.
        // For non-JSON clients (e.g., a browser request that asks for HTML only), let the platform's
        // default page render. Empty/missing Accept counts as "accept everything".
        if (!_AcceptsJsonProblemDetails(httpContext.Request))
        {
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

        bool written;
        try
        {
            written = await problemDetailsService
                .TryWriteAsync(
                    new ProblemDetailsContext
                    {
                        HttpContext = httpContext,
                        Exception = exception,
                        ProblemDetails = problemDetails,
                    }
                )
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // Cancellation has its own handling earlier in the switch — let it propagate.
            throw;
        }
#pragma warning disable CA1031 // Last-resort fallback path; primary writer failures must not re-throw out of the handler.
        catch (Exception primaryWriteError)
#pragma warning restore CA1031
        {
            _LogPrimaryWriteFailed(logger, primaryWriteError, exception.GetType().Name);
            return false;
        }

        if (written)
        {
            return true;
        }

        if (httpContext.Response.HasStarted)
        {
            _LogResponseAlreadyStarted(logger, exception.GetType().Name);
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
        return _DbUpdateConcurrencyTypeCache.GetValue(ex.GetType(), _DbUpdateConcurrencyFactory).Value;
    }

    private static bool _IsCancellationException(Exception? ex, int maxDepth = 20)
    {
        // Iterative walk capped at depth so a pathological/cyclic InnerException chain cannot blow
        // the stack. AggregateException's children are visited recursively with the remaining depth
        // budget so a pathological nested-AggregateException tree cannot exceed the same total cap.
        var depth = 0;

        while (ex is not null && depth < maxDepth)
        {
            if (ex is OperationCanceledException)
            {
                return true;
            }

            if (ex is AggregateException aggregate)
            {
                var remaining = maxDepth - depth - 1;

                if (remaining <= 0)
                {
                    return false;
                }

                foreach (var inner in aggregate.InnerExceptions)
                {
                    if (_IsCancellationException(inner, remaining))
                    {
                        return true;
                    }
                }
                return false;
            }

            ex = ex.InnerException;
            depth++;
        }

        return false;
    }

    private static bool _AcceptsJsonProblemDetails(HttpRequest request)
    {
        return request.CanAccept(ContentTypes.Applications.Json, ContentTypes.Applications.ProblemJson);
    }

    [LoggerMessage(
        EventId = 5002,
        EventName = "RequestCancelled",
        Level = LogLevel.Information,
        Message = "Client cancelled the request",
        SkipEnabledCheck = true
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
        EventId = 5010,
        EventName = CrossTenantWriteException.FailureCategoryName,
        Level = LogLevel.Warning,
        Message = "Cross-tenant write exception occurred",
        SkipEnabledCheck = true
    )]
    private static partial void _LogCrossTenantWriteException(ILogger logger, Exception exception);

    [LoggerMessage(
        EventId = 5011,
        EventName = "MissingTenantContext",
        Level = LogLevel.Warning,
        Message = "Missing tenant context for request: {ExceptionType} at {RequestPath}"
    )]
    private static partial void _LogMissingTenantContext(
        ILogger logger,
        Exception exception,
        string exceptionType,
        string requestPath
    );

    [LoggerMessage(
        EventId = 5012,
        EventName = "TenantResolutionMiddlewareMissing",
        Level = LogLevel.Warning,
        Message = "MissingTenantContextException was raised for {RequestPath} but TenantResolutionMiddleware "
            + "did not run for this request. Verify that UseHeadlessTenancy() is registered in the request "
            + "pipeline (after UseAuthentication() and before UseAuthorization())."
    )]
    private static partial void _LogTenantResolutionMiddlewareMissing(ILogger logger, string requestPath);

    [LoggerMessage(
        EventId = 5004,
        EventName = "RequestTimeoutException",
        Level = LogLevel.Warning,
        Message = "Request was timed out"
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

    [LoggerMessage(
        EventId = 5008,
        EventName = "PrimaryWriteFailed",
        Level = LogLevel.Warning,
        Message = "IProblemDetailsService.TryWriteAsync threw while writing ProblemDetails for {ExceptionType}; downstream handler or default response will run",
        SkipEnabledCheck = true
    )]
    private static partial void _LogPrimaryWriteFailed(ILogger logger, Exception exception, string exceptionType);

    [LoggerMessage(
        EventId = 5009,
        EventName = "ProblemDetailsCreatorFailed",
        Level = LogLevel.Warning,
        Message = "IProblemDetailsCreator factory threw while building ProblemDetails for {ExceptionType}; downstream handler or default response will run",
        SkipEnabledCheck = true
    )]
    private static partial void _LogCreatorFailed(ILogger logger, Exception exception, string exceptionType);
}
