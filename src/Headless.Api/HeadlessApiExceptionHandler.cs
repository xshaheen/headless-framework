// Copyright (c) Mahmoud Shaheen. All rights reserved.

using FluentValidation;
using Headless.Abstractions;
using Headless.Api.Abstractions;
using Headless.Api.Resources;
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
/// and <c>MinimalApiExceptionFilter</c>: a single mapping covers MVC actions, Minimal-API endpoints,
/// middleware, hosted services, and SignalR hubs.
/// </summary>
/// <remarks>
/// Information-disclosure invariant: response bodies surface only the framework-owned fields
/// produced by <see cref="IProblemDetailsCreator"/> plus the standard normalized extensions.
/// Exception messages, <see cref="System.Exception.Data"/>, and inner-exception content are NOT
/// surfaced — they belong in server logs.
/// </remarks>
internal sealed partial class HeadlessApiExceptionHandler(
    IOptions<JsonOptions> jsonOptions,
    IProblemDetailsService problemDetailsService,
    IProblemDetailsCreator problemDetailsCreator,
    ILogger<HeadlessApiExceptionHandler> logger
) : IExceptionHandler
{
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
            case not null
                when string.Equals(exception.GetType().Name, "DbUpdateConcurrencyException", StringComparison.Ordinal):
                _LogDbConcurrencyException(logger, exception);
                problemDetails = problemDetailsCreator.Conflict([GeneralMessageDescriber.ConcurrencyFailure()]);
                statusCode = StatusCodes.Status409Conflict;
                break;

            case TimeoutException:
                _LogRequestTimeoutException(logger, exception);
                problemDetails = new ProblemDetails
                {
                    Status = StatusCodes.Status408RequestTimeout,
                    Title = "Request Timeout",
                    Detail = "The request timed out",
                };
                problemDetailsCreator.Normalize(problemDetails);
                statusCode = StatusCodes.Status408RequestTimeout;
                break;

            case NotImplementedException:
                problemDetails = new ProblemDetails
                {
                    Status = StatusCodes.Status501NotImplemented,
                    Title = "Not Implemented",
                    Detail = "This functionality is not implemented",
                };
                problemDetailsCreator.Normalize(problemDetails);
                statusCode = StatusCodes.Status501NotImplemented;
                break;

            case OperationCanceledException:
            case { InnerException: OperationCanceledException }:
                // Client closed the request — status only, no body. The client is gone.
                if (httpContext.Response.HasStarted)
                {
                    return false;
                }
                httpContext.Response.StatusCode = StatusCodes.Status499ClientClosedRequest;
                return true;

            default:
                return false;
        }

        httpContext.Response.StatusCode = statusCode;

        var written = await problemDetailsService.TryWriteAsync(
            new ProblemDetailsContext { HttpContext = httpContext, ProblemDetails = problemDetails }
        );

        if (written)
        {
            return true;
        }

        if (httpContext.Response.HasStarted)
        {
            _LogResponseAlreadyStarted(logger, exception.GetType().Name);
            return false;
        }

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
}
