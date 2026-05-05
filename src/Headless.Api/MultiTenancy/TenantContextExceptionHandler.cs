// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Abstractions;
using Headless.Api.Abstractions;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Headless.Api.MultiTenancy;

/// <summary>
/// Maps <see cref="MissingTenantContextException"/> to a normalized 400 ProblemDetails response.
/// Delegates ProblemDetails construction to <see cref="IProblemDetailsCreator.TenantRequired"/>
/// so a single canonical shape is used by both the global exception pipeline and any direct caller.
/// </summary>
/// <remarks>
/// Surfaces only the framework-owned <c>code</c>, <c>type</c>, <c>title</c>, <c>detail</c>, and the
/// standard normalized extensions. The exception's <see cref="System.Exception.Message"/>,
/// <see cref="System.Exception.Data"/>, and <see cref="System.Exception.InnerException"/> are NOT
/// included in the response — they belong in server logs.
/// </remarks>
internal sealed partial class TenantContextExceptionHandler(
    IOptions<TenantContextProblemDetailsOptions> options,
    IProblemDetailsService problemDetailsService,
    IProblemDetailsCreator problemDetailsCreator,
    ILogger<TenantContextExceptionHandler> logger
) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(
        HttpContext httpContext,
        Exception exception,
        CancellationToken cancellationToken
    )
    {
        if (exception is not MissingTenantContextException)
        {
            return false;
        }

        var optionsValue = options.Value;
        var problemDetails = problemDetailsCreator.TenantRequired(optionsValue.TypeUriPrefix, optionsValue.ErrorCode);

        httpContext.Response.StatusCode = StatusCodes.Status400BadRequest;

        var written = await problemDetailsService.TryWriteAsync(
            new ProblemDetailsContext { HttpContext = httpContext, ProblemDetails = problemDetails }
        );

        if (written)
        {
            LogTenantRequired(logger, optionsValue.ErrorCode);
            return true;
        }

        if (httpContext.Response.HasStarted)
        {
            LogResponseAlreadyStarted(logger);
            return false;
        }

        httpContext.Response.ContentType = "application/problem+json";
        await httpContext.Response.WriteAsJsonAsync(
            problemDetails,
            options: null,
            contentType: "application/problem+json",
            cancellationToken: cancellationToken
        );
        LogTenantRequired(logger, optionsValue.ErrorCode);
        return true;
    }

    [LoggerMessage(
        EventId = 5005,
        EventName = "TenantContextRequired",
        Level = LogLevel.Warning,
        Message = "Request rejected: ambient tenant context required (errorCode: {ErrorCode})",
        SkipEnabledCheck = true
    )]
    private static partial void LogTenantRequired(ILogger logger, string errorCode);

    [LoggerMessage(
        EventId = 5006,
        EventName = "TenantContextResponseAlreadyStarted",
        Level = LogLevel.Error,
        Message = "Cannot map MissingTenantContextException: response has already started; client receives the partial response",
        SkipEnabledCheck = true
    )]
    private static partial void LogResponseAlreadyStarted(ILogger logger);
}
