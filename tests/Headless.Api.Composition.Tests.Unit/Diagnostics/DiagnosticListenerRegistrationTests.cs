// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Diagnostics;
using Headless.Api;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Tests.Diagnostics;

public sealed class DiagnosticListenerRegistrationTests
{
    [Fact]
    public async Task should_log_bad_request_until_composite_subscription_is_disposed()
    {
        // given
        using var listener = new DiagnosticListener("api-tests");
        using var loggerProvider = new CapturingLoggerProvider();
        var builder = WebApplication.CreateBuilder();
        builder.Logging.ClearProviders();
        builder.Logging.AddProvider(loggerProvider);
        builder.Services.AddSingleton(listener);
        await using var app = builder.Build();
        var features = new FeatureCollection();
        var exception = new BadHttpRequestException("invalid request line");
        features.Set<IBadRequestExceptionFeature>(new BadRequestExceptionFeature(exception));

        // when: Kestrel writes the connection's feature collection itself as the event payload
        var subscription = app.AddHeadlessApiDiagnosticListeners();
        listener.Write(DiagnosticSources.KestrelOnBadRequest, features);
        subscription.Dispose();
        listener.Write(DiagnosticSources.KestrelOnBadRequest, features);

        // then
        var entry = loggerProvider.Entries.Should().ContainSingle(item => item.EventId.Id == 5104).Which;
        entry.Level.Should().Be(LogLevel.Warning);
        entry.Exception.Should().BeSameAs(exception);
        entry.Message.Should().Be("Bad request received");
    }

    [Fact]
    public async Task should_log_middleware_analysis_events_written_as_anonymous_payloads()
    {
        // given
        using var listener = new DiagnosticListener("api-tests");
        using var loggerProvider = new CapturingLoggerProvider();
        var builder = WebApplication.CreateBuilder();
        builder.Logging.ClearProviders();
        builder.Logging.AddProvider(loggerProvider);
        builder.Services.AddSingleton(listener);
        await using var app = builder.Build();
        var context = new DefaultHttpContext();
        context.Request.Path = "/orders/42";
        context.Response.StatusCode = StatusCodes.Status202Accepted;
        var exception = new InvalidOperationException("middleware failed");

        // when: the same payload shapes AnalysisMiddleware writes
        using var subscription = app.AddMiddlewareAnalysisDiagnosticListeners();
        var enabled = listener.IsEnabled(DiagnosticSources.AnalysisOnMiddlewareStarting);
        var unrelatedEnabled = listener.IsEnabled(DiagnosticSources.KestrelOnBadRequest);
        listener.Write(
            DiagnosticSources.AnalysisOnMiddlewareStarting,
            new
            {
                name = "OrdersMiddleware",
                httpContext = (HttpContext)context,
                instanceId = Guid.NewGuid().ToString(),
                timestamp = 123L,
            }
        );
        listener.Write(
            DiagnosticSources.AnalysisOnMiddlewareFinished,
            new
            {
                name = "OrdersMiddleware",
                httpContext = (HttpContext)context,
                instanceId = Guid.NewGuid().ToString(),
                timestamp = 456L,
                duration = 17L,
            }
        );
        listener.Write(
            DiagnosticSources.AnalysisOnMiddlewareException,
            new
            {
                name = "OrdersMiddleware",
                httpContext = (HttpContext)context,
                instanceId = Guid.NewGuid().ToString(),
                timestamp = 789L,
                duration = 3L,
                exception,
            }
        );

        // then
        enabled.Should().BeTrue();
        unrelatedEnabled.Should().BeFalse();
        loggerProvider.Entries.Select(entry => entry.EventId.Id).Should().Equal(100, 101, 102);
        loggerProvider.Entries[0].Message.Should().Contain("OrdersMiddleware").And.Contain("/orders/42");
        loggerProvider.Entries[1].Message.Should().Contain("17").And.Contain("202");
        loggerProvider.Entries[2].Exception.Should().BeSameAs(exception);
    }

    private sealed class BadRequestExceptionFeature(Exception error) : IBadRequestExceptionFeature
    {
        public Exception Error { get; } = error;
    }

    private sealed class CapturingLoggerProvider : ILoggerProvider
    {
        public List<LogEntry> Entries { get; } = [];

        public ILogger CreateLogger(string categoryName)
        {
            return new CapturingLogger(Entries);
        }

        public void Dispose() { }
    }

    private sealed class CapturingLogger(List<LogEntry> entries) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull
        {
            return null;
        }

        public bool IsEnabled(LogLevel logLevel)
        {
            return true;
        }

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter
        )
        {
            entries.Add(new LogEntry(logLevel, eventId, exception, formatter(state, exception)));
        }
    }

    private sealed record LogEntry(LogLevel Level, EventId EventId, Exception? Exception, string Message);
}
