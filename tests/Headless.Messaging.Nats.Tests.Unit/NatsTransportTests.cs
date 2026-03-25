// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Messaging;
using Headless.Messaging.Messages;
using Headless.Messaging.Nats;
using Headless.Testing.Tests;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using MessagingHeaders = Headless.Messaging.Headers;

namespace Tests;

public sealed class NatsTransportTests : TestBase
{
    private readonly ILogger<NatsTransport> _logger;
    private readonly INatsConnectionPool _pool;

    public NatsTransportTests()
    {
        _logger = NullLogger<NatsTransport>.Instance;
        _pool = Substitute.For<INatsConnectionPool>();
        _pool.ServersAddress.Returns("nats://localhost:4222");
    }

    [Fact]
    public async Task should_have_correct_broker_address()
    {
        await using var transport = new NatsTransport(_logger, _pool);

        transport.BrokerAddress.Name.Should().Be("nats");
        transport.BrokerAddress.Endpoint.Should().Be("nats://localhost:4222");
    }

    [Fact]
    public async Task should_propagate_cancellation()
    {
        await using var transport = new NatsTransport(_logger, _pool);

        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        var act = async () => await transport.SendAsync(_CreateTransportMessage("msg-123", "TestMessage"), cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task should_dispose_without_error()
    {
        await using var transport = new NatsTransport(_logger, _pool);

        // ReSharper disable once DisposeOnUsingVariable
        // ReSharper disable once AccessToDisposedClosure
        var act = async () => await transport.DisposeAsync();

        await act.Should().NotThrowAsync();
    }

    private static TransportMessage _CreateTransportMessage(string messageId, string messageName)
    {
        return new TransportMessage(
            headers: new Dictionary<string, string?>(StringComparer.Ordinal)
            {
                { MessagingHeaders.MessageId, messageId },
                { MessagingHeaders.MessageName, messageName },
            },
            body: "test-body"u8.ToArray()
        );
    }
}
