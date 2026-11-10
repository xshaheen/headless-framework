// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Text;
using Headless.Messaging;
using Headless.Messaging.Internal;
using Headless.Testing.Tests;
using Microsoft.Extensions.Logging;

namespace Tests.Internal;

public sealed class ConsumerRegisterLogTests : TestBase
{
    [Fact]
    public void should_log_message_identity_and_never_the_body_when_processing_fails()
    {
        // given
        var logger = new CapturingLogger();
        var message = new TransportMessage(
            new Dictionary<string, string?>(StringComparer.Ordinal)
            {
                [Headers.MessageId] = "msg-42",
                [Headers.MessageName] = "orders.placed",
                ["x-customer-email"] = "alice@example.com",
            },
            Encoding.UTF8.GetBytes("""{"card":"4111111111111111"}""")
        );

        // when
        logger.LogProcessReceivedMessageFailed(new InvalidOperationException("boom"), message);

        // then
        var entry = logger.Messages.Should().ContainSingle().Subject;
        entry.Should().Contain("msg-42").And.Contain("orders.placed");
        entry.Should().NotContain("4111111111111111").And.NotContain("alice@example.com");
    }

    [Fact]
    public void should_log_without_throwing_when_the_failed_message_has_no_identity_headers()
    {
        // given
        var logger = new CapturingLogger();
        var message = new TransportMessage(
            new Dictionary<string, string?>(StringComparer.Ordinal),
            ReadOnlyMemory<byte>.Empty
        );

        // when
        var act = () => logger.LogProcessReceivedMessageFailed(new InvalidOperationException("boom"), message);

        // then
        act.Should().NotThrow();
        logger.Messages.Should().ContainSingle();
    }

    [Fact]
    public void should_strip_line_breaks_from_broker_supplied_identity_headers()
    {
        // given
        var logger = new CapturingLogger();
        var message = new TransportMessage(
            new Dictionary<string, string?>(StringComparer.Ordinal)
            {
                [Headers.MessageId] = "msg-1\nFAKE ENTRY",
                [Headers.MessageName] = "orders.placed",
            },
            ReadOnlyMemory<byte>.Empty
        );

        // when
        logger.LogProcessReceivedMessageFailed(new InvalidOperationException("boom"), message);

        // then
        logger.Messages.Should().ContainSingle().Which.Should().NotContain("\n");
    }

    private sealed class CapturingLogger : ILogger
    {
        public List<string> Messages { get; } = [];

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter
        ) => Messages.Add(formatter(state, exception));
    }
}
