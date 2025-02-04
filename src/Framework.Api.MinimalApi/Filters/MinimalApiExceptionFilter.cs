// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Text.RegularExpressions;
using FluentValidation;
using Framework.Api.Abstractions;
using Framework.Api.Resources;
using Framework.Constants;
using Framework.Exceptions;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

#pragma warning disable IDE0130
// ReSharper disable once CheckNamespace
namespace Microsoft.AspNetCore.Builder;

[PublicAPI]
public sealed partial class MinimalApiExceptionFilter(
    IProblemDetailsCreator problemDetailsCreator,
    ILogger<MinimalApiExceptionFilter> logger
) : IEndpointFilter
{
    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        // If the exception is not an API exception, we don't need to do anything.
        if (
            !context.HttpContext.Request.CanAccept(
                ContentTypes.Applications.Json,
                ContentTypes.Applications.ProblemJson
            )
        )
        {
            return await next(context);
        }

        try
        {
            return await next(context);
        }
        catch (ConflictException exception)
        {
            var details = problemDetailsCreator.Conflict(exception.Errors);

            return TypedResults.Problem(details);
        }
        catch (ValidationException exception)
        {
            var errors = exception.Errors.ToErrorDescriptors();
            var details = problemDetailsCreator.UnprocessableEntity(errors);

            return TypedResults.Problem(details);
        }
        catch (EntityNotFoundException exception)
        {
            var details = problemDetailsCreator.EntityNotFound(exception.Entity, exception.Key);

            return TypedResults.Problem(details);
        }
        // DB Concurrency
        catch (DbUpdateConcurrencyException exception)
        {
            LogDbConcurrencyException(logger, exception);

            var details = problemDetailsCreator.Conflict([GeneralMessageDescriber.ConcurrencyFailure()]);

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
            LogUnhandledException(logger, exception);

            if (exception.InnerException is OperationCanceledException)
            {
                return TypedResults.StatusCode(StatusCodes.Status499ClientClosedRequest);
            }

            throw;
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
