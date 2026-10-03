// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace Headless.Api.Diagnostics;

/// <summary>
/// Diagnostic observer that subscribes to middleware analysis events and writes structured log
/// entries for each middleware start, finish, and exception. Register with
/// <c>DiagnosticListener.Subscribe(observer, IsEnabled)</c> after calling
/// <see cref="AddMiddlewareAnalyzerFilterExtensions.AddMiddlewareAnalyzerFilter"/>.
/// </summary>
[PublicAPI]
public sealed partial class MiddlewareAnalysisDiagnosticAdapter(ILogger logger)
    : IObserver<KeyValuePair<string, object?>>
{
    /// <summary>Whether this observer handles the named diagnostic event.</summary>
    /// <param name="eventName">The diagnostic event name.</param>
    /// <returns><see langword="true"/> for the three middleware analysis events; otherwise <see langword="false"/>.</returns>
    public static bool IsEnabled(string eventName)
    {
        return eventName
            is DiagnosticSources.AnalysisOnMiddlewareStarting
                or DiagnosticSources.AnalysisOnMiddlewareFinished
                or DiagnosticSources.AnalysisOnMiddlewareException;
    }

    /// <summary>
    /// Dispatches a middleware analysis event to its handler. The analysis pipeline writes an anonymous payload,
    /// so its properties are read by name; an event whose payload lacks a required property is ignored.
    /// </summary>
    /// <param name="value">The event name and its payload.</param>
    public void OnNext(KeyValuePair<string, object?> value)
    {
        if (value.Value is not { } payload || !DiagnosticPayload.TryGet<string>(payload, "name", out var name))
        {
            return;
        }

        switch (value.Key)
        {
            case DiagnosticSources.AnalysisOnMiddlewareStarting
                when DiagnosticPayload.TryGet<HttpContext>(payload, "httpContext", out var httpContext)
                    && DiagnosticPayload.TryGet<long>(payload, "timestamp", out var timestamp):
                OnMiddlewareStarting(httpContext, name, timestamp);

                break;
            case DiagnosticSources.AnalysisOnMiddlewareFinished
                when DiagnosticPayload.TryGet<HttpContext>(payload, "httpContext", out var httpContext)
                    && DiagnosticPayload.TryGet<long>(payload, "timestamp", out var timestamp)
                    && DiagnosticPayload.TryGet<long>(payload, "duration", out var duration):
                OnMiddlewareFinished(httpContext, name, timestamp, duration);

                break;
            case DiagnosticSources.AnalysisOnMiddlewareException
                when DiagnosticPayload.TryGet<Exception>(payload, "exception", out var exception)
                    && DiagnosticPayload.TryGet<long>(payload, "timestamp", out var timestamp)
                    && DiagnosticPayload.TryGet<long>(payload, "duration", out var duration):
                OnMiddlewareException(exception, name, timestamp, duration);

                break;
        }
    }

    /// <inheritdoc />
    public void OnError(Exception error) { }

    /// <inheritdoc />
    public void OnCompleted() { }

    /// <summary>Handles the <see cref="DiagnosticSources.AnalysisOnMiddlewareStarting"/> event.</summary>
    public void OnMiddlewareStarting(HttpContext httpContext, string name, long timestamp)
    {
        Extensions.MiddlewareStarting(logger, timestamp, name, httpContext.Request.Path);
    }

    /// <summary>Handles the <see cref="DiagnosticSources.AnalysisOnMiddlewareFinished"/> event.</summary>
    public void OnMiddlewareFinished(HttpContext httpContext, string name, long timestamp, long duration)
    {
        Extensions.MiddlewareFinished(logger, timestamp, name, duration, httpContext.Response.StatusCode);
    }

    /// <summary>Handles the <see cref="DiagnosticSources.AnalysisOnMiddlewareException"/> event.</summary>
    public void OnMiddlewareException(Exception exception, string name, long timestamp, long duration)
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
