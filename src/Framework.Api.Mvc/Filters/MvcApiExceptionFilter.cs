// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Diagnostics;
using System.Text.RegularExpressions;
using FluentValidation;
using Framework.Api.Abstractions;
using Framework.Api.Resources;
using Framework.Constants;
using Framework.Exceptions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Framework.Api.Mvc.Filters;

[PublicAPI]
public sealed partial class MvcApiExceptionFilter(
    IProblemDetailsCreator problemDetailsCreator,
    ILogger<MvcApiExceptionFilter> logger
) : IAsyncExceptionFilter
{
    public async Task OnExceptionAsync(ExceptionContext context)
    {
        // If the exception is already handled, we don't need to do anything.
        if (context.ExceptionHandled)
        {
            return;
        }

        var httpContext = context.HttpContext;

        // If the exception is not an API exception, we don't need to do anything.
        if (!httpContext.Request.CanAccept(ContentTypes.Applications.Json, ContentTypes.Applications.ProblemJson))
        {
            return;
        }

        var task = context.Exception switch
        {
            ConflictException e => _Handle(httpContext, e),
            ValidationException e => _Handle(httpContext, e),
            EntityNotFoundException e => _Handle(httpContext, e),
            DbUpdateConcurrencyException e => _Handle(httpContext, e),
            TimeoutException e => _Handle(httpContext, e),
            NotImplementedException e => _Handle(httpContext, e),
            OperationCanceledException e => _Handle(httpContext, e),
            { InnerException: OperationCanceledException e } => _Handle(httpContext, e),
            _ => null,
        };

        if (task is not null)
        {
            await task;
            context.ExceptionHandled = true;
        }
    }

    private Task _Handle(HttpContext context, ValidationException exception)
    {
        var problemDetails = problemDetailsCreator.UnprocessableEntity(exception.Errors.ToErrorDescriptors());

        return Results.Problem(problemDetails).ExecuteAsync(context);
    }

    private Task _Handle(HttpContext context, ConflictException exception)
    {
        var problemDetails = problemDetailsCreator.Conflict(exception.Errors);

        return Results.Problem(problemDetails).ExecuteAsync(context);
    }

    private Task _Handle(HttpContext context, EntityNotFoundException exception)
    {
        var problemDetails = problemDetailsCreator.EntityNotFound(exception.Entity, exception.Key);

        return Results.Problem(problemDetails).ExecuteAsync(context);
    }

    private Task _Handle(HttpContext context, DbUpdateConcurrencyException exception)
    {
        LogDbConcurrencyException(logger, exception);

        var problemDetails = problemDetailsCreator.Conflict([GeneralMessageDescriber.ConcurrencyFailure()]);

        return Results.Problem(problemDetails).ExecuteAsync(context);
    }

    private Task _Handle(HttpContext context, TimeoutException exception)
    {
        LogRequestTimeoutException(logger, exception);

        return Results.Problem(statusCode: StatusCodes.Status408RequestTimeout).ExecuteAsync(context);
    }

    private static Task _Handle(HttpContext context, OperationCanceledException _)
    {
        return Results.Problem(statusCode: 499).ExecuteAsync(context);
    }

    private static Task _Handle(HttpContext context, NotImplementedException _)
    {
        return Results.Problem(statusCode: StatusCodes.Status501NotImplemented).ExecuteAsync(context);
    }

    [LoggerMessage(
        EventId = 5003,
        EventName = "DbConcurrencyException",
        Level = LogLevel.Critical,
        Message = "Database concurrency exception occurred",
        SkipEnabledCheck = true
    )]
    // ReSharper disable once InconsistentNaming
    private static partial void LogDbConcurrencyException(ILogger logger, Exception exception);

    [LoggerMessage(
        EventId = 5004,
        EventName = "RequestTimeoutException",
        Level = LogLevel.Debug,
        Message = "Request was timed out",
        SkipEnabledCheck = true
    )]
    // ReSharper disable once InconsistentNaming
    private static partial void LogRequestTimeoutException(ILogger logger, Exception exception);
}
