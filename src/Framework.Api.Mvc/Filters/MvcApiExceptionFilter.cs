using System.Diagnostics;
using System.Text.RegularExpressions;
using FluentValidation;
using Framework.Api.Core.Abstractions;
using Framework.Api.Core.Resources;
using Framework.BuildingBlocks;
using Framework.BuildingBlocks.Constants;
using Framework.FluentValidation;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Net.Http.Headers;

namespace Framework.Api.Mvc.Filters;

public sealed partial class MvcApiExceptionFilter : IExceptionFilter
{
    private readonly IProblemDetailsCreator _problemDetailsCreator;
    private readonly IHostEnvironment _environment;
    private readonly ILogger<MvcApiExceptionFilter> _logger;
    private readonly Dictionary<Type, Action<ExceptionContext>> _exceptionHandlers;

    public MvcApiExceptionFilter(
        IProblemDetailsCreator problemDetailsCreator,
        IHostEnvironment environment,
        ILogger<MvcApiExceptionFilter> logger
    )
    {
        _problemDetailsCreator = problemDetailsCreator;
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

    public void OnException(ExceptionContext context)
    {
        var type = context.Exception.GetType();

        if (_exceptionHandlers.TryGetValue(type, out var handler))
        {
            handler.Invoke(context);

            return;
        }

        _HandleUnknownException(context);
    }

    /// <summary>Handle validation exception.</summary>
    private void _HandleValidationException(ExceptionContext context)
    {
        var exception = context.Exception as ValidationException;

        Debug.Assert(exception is not null, nameof(exception) + " is not null");

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
                failureGroup => (IReadOnlyList<ErrorDescriptor>)failureGroup.ToArray(),
                StringComparer.Ordinal
            );

        var details = _problemDetailsCreator.UnprocessableEntity(context.HttpContext, errors);

        context.Result = new UnprocessableEntityObjectResult(details)
        {
            ContentTypes = [ContentTypes.Application.ProblemJson, ContentTypes.Application.ProblemXml],
        };

        context.HttpContext.Response.Headers[HeaderNames.CacheControl] = "no-cache, no-store, must-revalidate";
        context.HttpContext.Response.Headers[HeaderNames.Pragma] = "no-cache";
        context.HttpContext.Response.Headers[HeaderNames.ETag] = default;
        context.HttpContext.Response.Headers[HeaderNames.Expires] = "0";
        context.ExceptionHandled = true;
    }

    /// <summary>Handle entity not found response.</summary>
    private void _HandleEntityNotFoundException(ExceptionContext context)
    {
        var exception = context.Exception as EntityNotFoundException;
        Debug.Assert(exception is not null, nameof(exception) + " is not null");

        var details = _problemDetailsCreator.EntityNotFound(context.HttpContext, exception.Entity, exception.Key);
        context.Result = new NotFoundObjectResult(details);
        context.ExceptionHandled = true;
    }

    /// <summary>Handle forbidden request exception.</summary>
    private void _HandleConflictException(ExceptionContext context)
    {
        var exception = context.Exception as ConflictException;
        Debug.Assert(exception is not null, nameof(exception) + " is not null");

        var details = _problemDetailsCreator.Conflict(context.HttpContext, exception.Errors);
        context.Result = new ObjectResult(details) { StatusCode = StatusCodes.Status409Conflict, };
        context.ExceptionHandled = true;
    }

    /// <summary>Handle DbUpdateConcurrencyException.</summary>
    private void _HandleDbUpdateConcurrencyException(ExceptionContext context)
    {
        LogDbConcurrencyException(_logger, context.Exception);

        var details = _problemDetailsCreator.Conflict(
            context.HttpContext,
            [SharedMessageDescriber.General.ConcurrencyFailure()]
        );

        context.Result = new ObjectResult(details) { StatusCode = StatusCodes.Status409Conflict, };
        context.ExceptionHandled = true;
    }

    /// <summary>Handle OperationCanceledException.</summary>
    private static void _HandleRequestCanceledException(ExceptionContext context)
    {
        context.Result = new StatusCodeResult(499);
        context.ExceptionHandled = true;
    }

    /// <summary>Handle request unhandled exceptions.</summary>
    private void _HandleUnknownException(ExceptionContext context)
    {
        LogUnhandledException(_logger, context.Exception);

        if (context.Exception is { InnerException: OperationCanceledException })
        {
            _HandleRequestCanceledException(context);

            return;
        }

        _HandleInternalError(context);
    }

    /// <summary>Handle NotImplementedException.</summary>
    private static void _HandleNotImplementedException(ExceptionContext context)
    {
        context.Result = new StatusCodeResult(StatusCodes.Status501NotImplemented);
        context.ExceptionHandled = true;
    }

    /// <summary>Handle Timeout</summary>
    private static void _HandleTimeoutExceptions(ExceptionContext context)
    {
        context.Result = new StatusCodeResult(StatusCodes.Status408RequestTimeout);
        context.ExceptionHandled = true;
    }

    private void _HandleInternalError(ExceptionContext context)
    {
        if (_environment.IsDevelopmentOrTest())
        {
            return;
        }

        var details = _problemDetailsCreator.InternalError(
            context.HttpContext,
            context.Exception.ExpandExceptionMessage()
        );

        context.Result = new ObjectResult(details) { StatusCode = StatusCodes.Status500InternalServerError };
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
