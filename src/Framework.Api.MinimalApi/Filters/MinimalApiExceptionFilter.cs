using System.Text.RegularExpressions;
using FluentValidation;
using Framework.Api.Core.Abstractions;
using Framework.Api.Core.Resources;
using Framework.BuildingBlocks;
using Framework.FluentValidation;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Net.Http.Headers;

namespace Framework.Api.MinimalApi.Filters;

public sealed partial class MinimalApiExceptionFilter : IEndpointFilter
{
    private readonly ProblemDetailsCreator _problemDetailsCreator;
    private readonly IHostEnvironment _environment;
    private readonly ILogger<MinimalApiExceptionFilter> _logger;

    public MinimalApiExceptionFilter(
        ProblemDetailsCreator problemDetailsCreator,
        IHostEnvironment environment,
        ILogger<MinimalApiExceptionFilter> logger
    )
    {
        _problemDetailsCreator = problemDetailsCreator;
        _environment = environment;
        _logger = logger;
    }

    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        try
        {
            return await next(context);
        }
        catch (ConflictException exception)
        {
            var details = _problemDetailsCreator.Conflict(context.HttpContext, exception.Errors);

            return TypedResults.Problem(details);
        }
        catch (ValidationException exception)
        {
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

            var details = _problemDetailsCreator.UnprocessableEntity(context.HttpContext, errors);

            context.HttpContext.Response.Headers[HeaderNames.CacheControl] = "no-cache, no-store, must-revalidate";
            context.HttpContext.Response.Headers[HeaderNames.Pragma] = "no-cache";
            context.HttpContext.Response.Headers[HeaderNames.ETag] = default;
            context.HttpContext.Response.Headers[HeaderNames.Expires] = "0";

            return TypedResults.Problem(details);
        }
        catch (EntityNotFoundException exception)
        {
            var details = _problemDetailsCreator.EntityNotFound(context.HttpContext, exception.Entity, exception.Key);

            return TypedResults.Problem(details);
        }
        // DB Concurrency
        catch (DbUpdateConcurrencyException exception)
        {
            LogDbConcurrencyException(_logger, exception);

            var details = _problemDetailsCreator.Conflict(
                context.HttpContext,
                [SharedMessageDescriber.General.ConcurrencyFailure()]
            );

            return TypedResults.Problem(details);
        }
        // Request canceled
        catch (OperationCanceledException)
        {
            return TypedResults.StatusCode(StatusCodes.Status499ClientClosedRequest);
        }
        // Timeout
        catch (RegexMatchTimeoutException)
        {
            return TypedResults.StatusCode(StatusCodes.Status408RequestTimeout);
        }
        // Not implemented
        catch (NotImplementedException)
        {
            return TypedResults.StatusCode(StatusCodes.Status501NotImplemented);
        }
        // Unknown exception
        catch (Exception exception)
        {
            LogUnhandledException(_logger, exception);

            if (exception is { InnerException: OperationCanceledException })
            {
                return TypedResults.StatusCode(StatusCodes.Status499ClientClosedRequest);
            }

            if (_environment.IsDevelopmentOrTest())
            {
                return TypedResults.StatusCode(StatusCodes.Status500InternalServerError);
            }

            var details = _problemDetailsCreator.InternalError(context.HttpContext, exception.ExpandExceptionMessage());

            return TypedResults.Problem(details);
        }
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
