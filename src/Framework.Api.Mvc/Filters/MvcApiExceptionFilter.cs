// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Diagnostics;
using System.Text.RegularExpressions;
using FluentValidation;
using Framework.Api.Abstractions;
using Framework.Api.Resources;
using Framework.Constants;
using Framework.Exceptions;
using Framework.FluentValidation;
using Framework.Primitives;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Framework.Api.Mvc.Filters;

[PublicAPI]
public sealed partial class MvcApiExceptionFilter : IAsyncExceptionFilter
{
    private readonly IFrameworkProblemDetailsFactory _problemDetailsFactory;
    private readonly IHostEnvironment _environment;
    private readonly ILogger<MvcApiExceptionFilter> _logger;
    private readonly Dictionary<Type, Func<ExceptionContext, Task>> _exceptionHandlers;

    public MvcApiExceptionFilter(
        IFrameworkProblemDetailsFactory problemDetailsFactory,
        IHostEnvironment environment,
        ILogger<MvcApiExceptionFilter> logger
    )
    {
        _problemDetailsFactory = problemDetailsFactory;
        _environment = environment;
        _logger = logger;

        // Register known exception types and handlers.
        _exceptionHandlers = new()
        {
            { typeof(ValidationException), _HandleValidationException },
            { typeof(ConflictException), _HandleConflictException },
            { typeof(DbUpdateConcurrencyException), _HandleDbUpdateConcurrencyException },
            { typeof(EntityNotFoundException), _HandleEntityNotFoundException },
            { typeof(OperationCanceledException), _HandleRequestCanceledException },
            { typeof(RegexMatchTimeoutException), _HandleTimeoutExceptions },
            { typeof(NotImplementedException), _HandleNotImplementedException },
        };
    }

    public async Task OnExceptionAsync(ExceptionContext context)
    {
        // If the exception is already handled, we don't need to do anything.
        if (context.ExceptionHandled)
        {
            return;
        }

        // If the exception is not an API exception, we don't need to do anything.
        if (
            !context.HttpContext.Request.CanAccept(
                ContentTypes.Applications.Json,
                ContentTypes.Applications.ProblemJson
            )
        )
        {
            return;
        }

        var type = context.Exception.GetType();

        if (_exceptionHandlers.TryGetValue(type, out var handler))
        {
            await handler(context);

            return;
        }

        await _HandleUnknownException(context);
    }

    /// <summary>Handle validation exception.</summary>
    private async Task _HandleValidationException(ExceptionContext context)
    {
        var exception = context.Exception as ValidationException;

        Debug.Assert(exception is not null);

        var errors = exception
            .Errors.GroupBy(
                failure => failure.PropertyName,
                failure => new ErrorDescriptor(
                    code: FluentValidationErrorCodeMapper.MapToApplicationErrorCode(failure.ErrorCode),
                    description: failure.ErrorMessage,
                    paramsDictionary: failure.FormattedMessagePlaceholderValues
                ),
                StringComparer.Ordinal
            )
            .ToDictionary(
                failureGroup => failureGroup.Key,
                failureGroup => (IReadOnlyList<ErrorDescriptor>)[.. failureGroup],
                StringComparer.Ordinal
            );

        var problemDetails = _problemDetailsFactory.UnprocessableEntity(context.HttpContext, errors);
        await Results.Problem(problemDetails).ExecuteAsync(context.HttpContext);
        context.ExceptionHandled = true;
    }

    /// <summary>Handle entity not found response.</summary>
    private async Task _HandleEntityNotFoundException(ExceptionContext context)
    {
        var exception = context.Exception as EntityNotFoundException;
        Debug.Assert(exception is not null);

        var problemDetails = _problemDetailsFactory.EntityNotFound(
            context.HttpContext,
            exception.Entity,
            exception.Key
        );

        await Results.Problem(problemDetails).ExecuteAsync(context.HttpContext);

        context.ExceptionHandled = true;
    }

    /// <summary>Handle forbidden request exception.</summary>
    private async Task _HandleConflictException(ExceptionContext context)
    {
        var exception = context.Exception as ConflictException;
        Debug.Assert(exception is not null);

        var problemDetails = _problemDetailsFactory.Conflict(context.HttpContext, exception.Errors);
        await Results.Problem(problemDetails).ExecuteAsync(context.HttpContext);
        context.ExceptionHandled = true;
    }

    /// <summary>Handle DbUpdateConcurrencyException.</summary>
    private async Task _HandleDbUpdateConcurrencyException(ExceptionContext context)
    {
        LogDbConcurrencyException(_logger, context.Exception);

        var problemDetails = _problemDetailsFactory.Conflict(
            context.HttpContext,
            [GeneralMessageDescriber.ConcurrencyFailure()]
        );

        await Results.Problem(problemDetails).ExecuteAsync(context.HttpContext);
        context.ExceptionHandled = true;
    }

    /// <summary>Handle OperationCanceledException.</summary>
    private static async Task _HandleRequestCanceledException(ExceptionContext context)
    {
        await Results.Problem(statusCode: 499).ExecuteAsync(context.HttpContext);
        context.ExceptionHandled = true;
    }

    /// <summary>Handle request unhandled exceptions.</summary>
    private Task _HandleUnknownException(ExceptionContext context)
    {
        LogUnhandledException(_logger, context.Exception);

        return context.Exception is { InnerException: OperationCanceledException }
            ? _HandleRequestCanceledException(context)
            : _HandleInternalError(context);
    }

    /// <summary>Handle NotImplementedException.</summary>
    private static async Task _HandleNotImplementedException(ExceptionContext context)
    {
        await Results.Problem(statusCode: StatusCodes.Status501NotImplemented).ExecuteAsync(context.HttpContext);

        context.ExceptionHandled = true;
    }

    /// <summary>Handle Timeout</summary>
    private static async Task _HandleTimeoutExceptions(ExceptionContext context)
    {
        await Results.Problem(statusCode: StatusCodes.Status408RequestTimeout).ExecuteAsync(context.HttpContext);

        context.ExceptionHandled = true;
    }

    private async Task _HandleInternalError(ExceptionContext context)
    {
        if (_environment.IsDevelopmentOrTest())
        {
            return;
        }

        var problemDetails = _problemDetailsFactory.InternalError(
            context.HttpContext,
            context.Exception.ExpandMessage()
        );

        await Results.Problem(problemDetails).ExecuteAsync(context.HttpContext);

        context.ExceptionHandled = true;
    }

    [LoggerMessage(
        EventId = 5001,
        EventName = "UnhandledException",
        Level = LogLevel.Critical,
        Message = "Unexpected exception occured.",
        SkipEnabledCheck = true
    )]
    // ReSharper disable once InconsistentNaming
    private static partial void LogUnhandledException(ILogger logger, Exception exception);

    [LoggerMessage(
        EventId = 5003,
        EventName = "DbConcurrencyException",
        Level = LogLevel.Critical,
        Message = "Database concurrency exception occurred",
        SkipEnabledCheck = true
    )]
    // ReSharper disable once InconsistentNaming
    private static partial void LogDbConcurrencyException(ILogger logger, Exception exception);
}
