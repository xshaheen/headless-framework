// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DiagnosticAdapter;
using Microsoft.Extensions.Logging;

#pragma warning disable IDE0060
namespace Headless.Api.Diagnostics;

/// <summary>
/// Diagnostic adapter that subscribes to middleware analysis events and writes structured log
/// entries for each middleware start, finish, and exception. Register with
/// <c>DiagnosticListener.SubscribeWithAdapter</c> after calling
/// <see cref="AddMiddlewareAnalyzerFilterExtensions.AddMiddlewareAnalyzerFilter"/>.
/// </summary>
[PublicAPI]
public sealed partial class MiddlewareAnalysisDiagnosticAdapter(ILogger logger)
{
    /// <summary>Handles the <see cref="DiagnosticSources.AnalysisOnMiddlewareStarting"/> event.</summary>
    [DiagnosticName(DiagnosticSources.AnalysisOnMiddlewareStarting)]
    public void OnMiddlewareStarting(HttpContext httpContext, string name, Guid instance, long timestamp)
    {
        Extensions.MiddlewareStarting(logger, timestamp, name, httpContext.Request.Path);
    }

    /// <summary>Handles the <see cref="DiagnosticSources.AnalysisOnMiddlewareFinished"/> event.</summary>
    [DiagnosticName(DiagnosticSources.AnalysisOnMiddlewareFinished)]
    public void OnMiddlewareFinished(HttpContext httpContext, string name, Guid instance, long timestamp, long duration)
    {
        Extensions.MiddlewareFinished(logger, timestamp, name, duration, httpContext.Response.StatusCode);
    }

    /// <summary>Handles the <see cref="DiagnosticSources.AnalysisOnMiddlewareException"/> event.</summary>
    [DiagnosticName(DiagnosticSources.AnalysisOnMiddlewareException)]
    public void OnMiddlewareException(
        Exception exception,
        HttpContext httpContext,
        string name,
        Guid instance,
        long timestamp,
        long duration
    )
    {
        if (!logger.IsEnabled(LogLevel.Information))
        {
            return;
        }

        var message = exception.ExpandMessage();
        Extensions.MiddlewareException(logger, exception, timestamp, name, duration, message);
    }

    private static partial class Extensions
    {
        [LoggerMessage(
            EventId = 100,
            EventName = "MiddlewareStarting",
            Level = LogLevel.Information,
            Message = "Middleware(Starting): '{Name}' Request Path: '{Path}' TimeStamp: {Timestamp}",
            SkipEnabledCheck = false
        )]
        public static partial void MiddlewareStarting(ILogger logger, long timestamp, string name, PathString path);

        [LoggerMessage(
            EventId = 101,
            EventName = "MiddlewareFinished",
            Level = LogLevel.Information,
            Message = "Middleware(Finished): '{Name}' Duration: {Duration} Status: '{StatusCode}' TimeStamp: {Timestamp}",
            SkipEnabledCheck = false
        )]
        public static partial void MiddlewareFinished(
            ILogger logger,
            long timestamp,
            string name,
            long duration,
            int statusCode
        );

        [LoggerMessage(
            EventId = 102,
            EventName = "MiddlewareException",
            Level = LogLevel.Information,
            Message = "Middleware(Exception): '{Name}' Duration: {Duration} '{Message}' TimeStamp: {Timestamp}",
            SkipEnabledCheck = false
        )]
        public static partial void MiddlewareException(
            ILogger logger,
            Exception exception,
            long timestamp,
            string name,
            long duration,
            string message
        );
    }
}
