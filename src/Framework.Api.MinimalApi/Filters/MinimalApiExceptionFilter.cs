// Copyright (c) Mahmoud Shaheen. All rights reserved.

using FluentValidation;
using Framework.Api.Abstractions;
using Framework.Api.Resources;
using Framework.Constants;
using Framework.Exceptions;
using Microsoft.AspNetCore.Http;
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
            return await next(context).AnyContext();
        }

        try
        {
            return await next(context).AnyContext();
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
        // DB Concurrency (type name match to avoid EF Core dependency)
        catch (Exception exception) when (exception.GetType().Name == "DbUpdateConcurrencyException")
        {
            LogDbConcurrencyException(logger, exception);

            var details = problemDetailsCreator.Conflict([GeneralMessageDescriber.ConcurrencyFailure()]);

            return TypedResults.Problem(details);
        }
        // Timeout
        catch (TimeoutException)
        {
            return TypedResults.Problem(
                statusCode: StatusCodes.Status408RequestTimeout,
                title: "Request Timeout",
                detail: "The request timed out"
            );
        }
        // Not implemented
        catch (NotImplementedException)
        {
            return TypedResults.Problem(
                statusCode: StatusCodes.Status501NotImplemented,
                title: "Not Implemented",
                detail: "This functionality is not implemented"
            );
        }
        // Request canceled
        catch (OperationCanceledException)
        {
            return TypedResults.StatusCode(StatusCodes.Status499ClientClosedRequest);
        }
        // Unknown exception
        catch (Exception exception) when (exception.InnerException is OperationCanceledException)
        {
            return TypedResults.StatusCode(StatusCodes.Status499ClientClosedRequest);
        }
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
}
