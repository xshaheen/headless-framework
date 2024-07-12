using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DiagnosticAdapter;
using Microsoft.Extensions.Logging;

#pragma warning disable IDE0060
namespace Framework.Api.Core.Diagnostics;

[PublicAPI]
public sealed partial class MiddlewareAnalysisDiagnosticAdapter(ILogger logger)
{
    [DiagnosticName(DiagnosticSources.MiddlewareAnalysisOnMiddlewareStarting)]
    public void OnMiddlewareStarting(HttpContext httpContext, string name, Guid instance, long timestamp)
    {
        Extensions.MiddlewareStarting(logger, timestamp, name, httpContext.Request.Path);
    }

    [DiagnosticName(DiagnosticSources.MiddlewareAnalysisOnMiddlewareFinished)]
    public void OnMiddlewareFinished(HttpContext httpContext, string name, Guid instance, long timestamp, long duration)
    {
        Extensions.MiddlewareFinished(logger, timestamp, name, duration, httpContext.Response.StatusCode);
    }

    [DiagnosticName(DiagnosticSources.MiddlewareAnalysisOnMiddlewareException)]
    public void OnMiddlewareException(
        Exception exception,
        HttpContext httpContext,
        string name,
        Guid instance,
        long timestamp,
        long duration
    )
    {
        Extensions.MiddlewareException(
            logger,
            exception,
            timestamp,
            name,
            duration,
            exception.ExpandExceptionMessage()
        );
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
