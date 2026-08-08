// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Diagnostics;
using Headless.Api;
using Headless.Api.Diagnostics;
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
        var listener = new DiagnosticListener("api-tests");
        var loggerProvider = new CapturingLoggerProvider();
        var builder = WebApplication.CreateBuilder();
        builder.Logging.ClearProviders();
        builder.Logging.AddProvider(loggerProvider);
        builder.Services.AddSingleton(listener);
        await using var app = builder.Build();
        var features = new FeatureCollection();
        var exception = new BadHttpRequestException("invalid request line");
        features.Set<IBadRequestExceptionFeature>(new BadRequestExceptionFeature(exception));

        // when
        var subscription = app.AddHeadlessApiDiagnosticListeners();
        var payload = new
        {
            value = new KeyValuePair<string, object?>(DiagnosticSources.KestrelOnBadRequest, features),
        };
        listener.Write(DiagnosticSources.KestrelOnBadRequest, payload);
        subscription.Dispose();
        listener.Write(DiagnosticSources.KestrelOnBadRequest, payload);

        // then
        var entry = loggerProvider.Entries.Should().ContainSingle(item => item.EventId.Id == 5104).Which;
        entry.Level.Should().Be(LogLevel.Warning);
        entry.Exception.Should().BeSameAs(exception);
        entry.Message.Should().Be("Bad request received");
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
