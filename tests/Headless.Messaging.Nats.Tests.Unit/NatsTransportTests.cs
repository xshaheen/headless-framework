// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Framework.Testing.Tests;
using Headless.Messaging.Internal;
using Headless.Messaging.Messages;
using Headless.Messaging.Nats;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using NATS.Client;
using NATS.Client.JetStream;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using MessagingHeaders = Headless.Messaging.Messages.Headers;

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
        // given, when
        await using var transport = new NatsTransport(_logger, _pool);

        // then
        transport.BrokerAddress.Name.Should().Be("NATS");
        transport.BrokerAddress.Endpoint.Should().Be("nats://localhost:4222");
    }

    [Fact]
    public async Task should_rent_connection_when_sending()
    {
        // given
        await using var transport = new NatsTransport(_logger, _pool);
        var message = _CreateTransportMessage("msg-123", "TestMessage");

        var connection = Substitute.For<IConnection>();
        var jetStream = Substitute.For<IJetStream>();
        _pool.RentConnection().Returns(connection);
        connection.CreateJetStreamContext(Arg.Any<JetStreamOptions>()).Returns(jetStream);
        jetStream
            .PublishAsync(Arg.Any<Msg>(), Arg.Any<PublishOptions>())
            .Throws(new NATSException("Connection closed")); // Force an exception to test the flow

        // when
        await transport.SendAsync(message);

        // then
        _pool.Received(1).RentConnection();
        _pool.Received(1).Return(connection);
    }

    [Fact]
    public async Task should_return_connection_to_pool_on_exception()
    {
        // given
        await using var transport = new NatsTransport(_logger, _pool);
        var message = _CreateTransportMessage("msg-123", "TestMessage");

        var connection = Substitute.For<IConnection>();
        var jetStream = Substitute.For<IJetStream>();
        _pool.RentConnection().Returns(connection);
        connection.CreateJetStreamContext(Arg.Any<JetStreamOptions>()).Returns(jetStream);
        jetStream
            .PublishAsync(Arg.Any<Msg>(), Arg.Any<PublishOptions>())
            .Throws(new InvalidOperationException("Publish failed"));

        // when
        var result = await transport.SendAsync(message);

        // then
        result.Succeeded.Should().BeFalse();
        result.Exception.Should().BeOfType<PublisherSentFailedException>();
        _pool.Received(1).Return(connection);
    }

    [Fact]
    public async Task should_wrap_nats_exception_in_publisher_failed_exception()
    {
        // given
        await using var transport = new NatsTransport(_logger, _pool);
        var message = _CreateTransportMessage("msg-123", "TestMessage");

        var connection = Substitute.For<IConnection>();
        var jetStream = Substitute.For<IJetStream>();
        _pool.RentConnection().Returns(connection);
        connection.CreateJetStreamContext(Arg.Any<JetStreamOptions>()).Returns(jetStream);
        jetStream
            .PublishAsync(Arg.Any<Msg>(), Arg.Any<PublishOptions>())
            .Throws(new NATSException("NATS connection closed"));

        // when
        var result = await transport.SendAsync(message);

        // then
        result.Succeeded.Should().BeFalse();
        result.Exception.Should().BeOfType<PublisherSentFailedException>();
        result.Exception!.InnerException.Should().BeOfType<NATSException>();
    }

    [Fact]
    public async Task should_pass_message_headers_to_nats()
    {
        // given
        await using var transport = new NatsTransport(_logger, _pool);
        var message = new TransportMessage(
            headers: new Dictionary<string, string?>(StringComparer.Ordinal)
            {
                { MessagingHeaders.MessageId, "msg-123" },
                { MessagingHeaders.MessageName, "TestMessage" },
                { "CustomHeader", "CustomValue" },
            },
            body: "test-body"u8.ToArray()
        );

        var connection = Substitute.For<IConnection>();
        var jetStream = Substitute.For<IJetStream>();
        _pool.RentConnection().Returns(connection);
        connection.CreateJetStreamContext(Arg.Any<JetStreamOptions>()).Returns(jetStream);

        Msg? capturedMsg = null;
        jetStream
            .PublishAsync(Arg.Do<Msg>(m => capturedMsg = m), Arg.Any<PublishOptions>())
            .Throws(new NATSException("Test exception")); // Force exception after capture

        // when
        await transport.SendAsync(message);

        // then
        capturedMsg.Should().NotBeNull();
        capturedMsg!.Header["CustomHeader"].Should().Be("CustomValue");
        capturedMsg.Header[MessagingHeaders.MessageId].Should().Be("msg-123");
    }

    [Fact]
    public async Task should_use_message_name_as_subject()
    {
        // given
        await using var transport = new NatsTransport(_logger, _pool);
        var message = _CreateTransportMessage("msg-123", "orders.created");

        var connection = Substitute.For<IConnection>();
        var jetStream = Substitute.For<IJetStream>();
        _pool.RentConnection().Returns(connection);
        connection.CreateJetStreamContext(Arg.Any<JetStreamOptions>()).Returns(jetStream);

        Msg? capturedMsg = null;
        jetStream
            .PublishAsync(Arg.Do<Msg>(m => capturedMsg = m), Arg.Any<PublishOptions>())
            .Throws(new NATSException("Test exception")); // Force exception after capture

        // when
        await transport.SendAsync(message);

        // then
        capturedMsg.Should().NotBeNull();
        capturedMsg!.Subject.Should().Be("orders.created");
    }

    [Fact]
    public async Task should_use_message_id_in_publish_options()
    {
        // given
        await using var transport = new NatsTransport(_logger, _pool);
        var message = _CreateTransportMessage("unique-msg-id", "TestMessage");

        var connection = Substitute.For<IConnection>();
        var jetStream = Substitute.For<IJetStream>();
        _pool.RentConnection().Returns(connection);
        connection.CreateJetStreamContext(Arg.Any<JetStreamOptions>()).Returns(jetStream);

        PublishOptions? capturedOptions = null;
        jetStream
            .PublishAsync(Arg.Any<Msg>(), Arg.Do<PublishOptions>(o => capturedOptions = o))
            .Throws(new NATSException("Test exception")); // Force exception after capture

        // when
        await transport.SendAsync(message);

        // then
        capturedOptions.Should().NotBeNull();
        capturedOptions!.MessageId.Should().Be("unique-msg-id");
    }

    [Fact]
    public async Task should_dispose_without_error()
    {
        // given
        var transport = new NatsTransport(_logger, _pool);

        // when
        var act = async () => await transport.DisposeAsync();

        // then
        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task should_include_message_body_in_nats_message()
    {
        // given
        await using var transport = new NatsTransport(_logger, _pool);
        var expectedBody = "test message content"u8.ToArray();
        var message = new TransportMessage(
            headers: new Dictionary<string, string?>(StringComparer.Ordinal)
            {
                { MessagingHeaders.MessageId, "msg-123" },
                { MessagingHeaders.MessageName, "TestMessage" },
            },
            body: expectedBody
        );

        var connection = Substitute.For<IConnection>();
        var jetStream = Substitute.For<IJetStream>();
        _pool.RentConnection().Returns(connection);
        connection.CreateJetStreamContext(Arg.Any<JetStreamOptions>()).Returns(jetStream);

        Msg? capturedMsg = null;
        jetStream
            .PublishAsync(Arg.Do<Msg>(m => capturedMsg = m), Arg.Any<PublishOptions>())
            .Throws(new NATSException("Test exception")); // Force exception after capture

        // when
        await transport.SendAsync(message);

        // then
        capturedMsg.Should().NotBeNull();
        capturedMsg!.Data.Should().BeEquivalentTo(expectedBody);
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
