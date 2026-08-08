// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Api.Diagnostics;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace Tests.Diagnostics;

public sealed class MiddlewareAnalysisDiagnosticAdapterTests
{
    [Fact]
    public void should_log_structured_start_and_finish_events()
    {
        // given
        var logger = new CapturingLogger();
        var sut = new MiddlewareAnalysisDiagnosticAdapter(logger);
        var context = new DefaultHttpContext();
        context.Request.Path = "/orders/42";
        context.Response.StatusCode = StatusCodes.Status202Accepted;

        // when
        sut.OnMiddlewareStarting(context, "OrdersMiddleware", Guid.Empty, 123);
        sut.OnMiddlewareFinished(context, "OrdersMiddleware", Guid.Empty, 456, 17);

        // then
        logger.Entries.Should().HaveCount(2);
        logger.Entries[0].EventId.Should().Be(new EventId(100, "MiddlewareStarting"));
        logger.Entries[0].Message.Should().Contain("OrdersMiddleware").And.Contain("/orders/42").And.Contain("123");
        logger.Entries[1].EventId.Should().Be(new EventId(101, "MiddlewareFinished"));
        logger.Entries[1].Message.Should().Contain("OrdersMiddleware").And.Contain("17").And.Contain("202");
    }

    [Fact]
    public void should_log_expanded_exception_details_when_information_enabled()
    {
        // given
        var logger = new CapturingLogger();
        var sut = new MiddlewareAnalysisDiagnosticAdapter(logger);
        var exception = new InvalidOperationException("outer", new ArgumentException("inner"));

        // when
        sut.OnMiddlewareException(exception, new DefaultHttpContext(), "FailureMiddleware", Guid.Empty, 9, 3);

        // then
        var entry = logger.Entries.Should().ContainSingle().Which;
        entry.EventId.Should().Be(new EventId(102, "MiddlewareException"));
        entry.Exception.Should().BeSameAs(exception);
        entry.Message.Should().Contain("FailureMiddleware").And.Contain("outer").And.Contain("inner");
    }

    [Fact]
    public void should_not_expand_or_log_exception_when_information_disabled()
    {
        // given
        var logger = new CapturingLogger { Enabled = false };
        var sut = new MiddlewareAnalysisDiagnosticAdapter(logger);

        // when
        sut.OnMiddlewareException(
            new InvalidOperationException("ignored"),
            new DefaultHttpContext(),
            "DisabledMiddleware",
            Guid.Empty,
            1,
            2
        );

        // then
        logger.Entries.Should().BeEmpty();
    }

    private sealed class CapturingLogger : ILogger
    {
        public bool Enabled { get; init; } = true;
        public List<LogEntry> Entries { get; } = [];

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull
        {
            return null;
        }

        public bool IsEnabled(LogLevel logLevel)
        {
            return Enabled;
        }

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter
        )
        {
            Entries.Add(new LogEntry(logLevel, eventId, exception, formatter(state, exception)));
        }

        public sealed record LogEntry(LogLevel Level, EventId EventId, Exception? Exception, string Message);
    }
}
