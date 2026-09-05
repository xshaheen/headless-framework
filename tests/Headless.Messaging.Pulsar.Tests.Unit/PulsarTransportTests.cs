// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Messaging;
using Headless.Messaging.Pulsar;
using Headless.Testing.Tests;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute.ExceptionExtensions;
using MessagingHeaders = Headless.Messaging.Headers;

namespace Tests;

public sealed class PulsarTransportTests : TestBase
{
    private readonly ILogger<PulsarTransport> _logger;
    private readonly IConnectionFactory _connectionFactory;

    public PulsarTransportTests()
    {
        _logger = NullLogger<PulsarTransport>.Instance;
        _connectionFactory = Substitute.For<IConnectionFactory>();
        _connectionFactory.ServersAddress.Returns("pulsar://localhost:6650");
    }

    [Fact]
    public async Task should_pass_affinity_to_native_producer_message_builder()
    {
        var producer = Substitute.For<global::Pulsar.Client.Api.IProducer<byte[]>>();
        _connectionFactory.CreateProducerAsync("headless-bus-orders").Returns(producer);
        await using var transport = new PulsarTransport(_logger, _connectionFactory);
        var message = new TransportMessage(
            new Dictionary<string, string?>(StringComparer.Ordinal)
            {
                [MessagingHeaders.MessageId] = "message-1",
                [MessagingHeaders.MessageName] = "orders",
                [MessagingHeaders.RoutingAffinityKey] = "order-42",
            },
            "payload"u8.ToArray()
        );

        await transport.SendAsync(message, AbortToken);

        producer.Received(1).NewMessage(Arg.Any<byte[]>(), "order-42", Arg.Any<IReadOnlyDictionary<string, string>>());
    }

    [Fact]
    public async Task should_reject_conflicting_affinity_before_creating_producer()
    {
        await using var transport = new PulsarTransport(_logger, _connectionFactory);
        var message = new TransportMessage(
            new Dictionary<string, string?>(StringComparer.Ordinal)
            {
                [MessagingHeaders.MessageId] = "message-1",
                [MessagingHeaders.MessageName] = "orders",
                [MessagingHeaders.RoutingAffinityKey] = "order-42",
                [PulsarMessagingHeaders.PulsarKey] = "other",
            },
            default
        );

        var result = await transport.SendAsync(message, AbortToken);

        result.Succeeded.Should().BeFalse();
        await _connectionFactory.DidNotReceive().CreateProducerAsync(Arg.Any<string>());
    }

    [Theory]
    [InlineData(null)]
    [InlineData("order-42")]
    public void should_resolve_native_affinity_key(string? raw)
    {
        var headers = new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            [MessagingHeaders.RoutingAffinityKey] = "order-42",
        };
        if (raw is not null)
        {
            headers[PulsarMessagingHeaders.PulsarKey] = raw;
        }

        PulsarRoutingAffinity.Mapping.ResolveKey(new TransportMessage(headers, default)).Should().Be("order-42");
    }

    [Fact]
    public async Task should_have_correct_broker_address()
    {
        // given, when
        await using var transport = new PulsarTransport(_logger, _connectionFactory);

        // then
        transport.BrokerAddress.Name.Should().Be("pulsar");
        transport.BrokerAddress.Endpoint.Should().Be("pulsar://localhost:6650");
    }

    [Fact]
    public async Task should_request_producer_for_message_topic()
    {
        // given
        await using var transport = new PulsarTransport(_logger, _connectionFactory);
        var message = _CreateTransportMessage("msg-123", "TestTopic");

        _connectionFactory.CreateProducerAsync("TestTopic").ThrowsAsync(new InvalidOperationException("Expected"));

        // when
        var result = await transport.SendAsync(message, AbortToken);

        // then
        result.Succeeded.Should().BeFalse();
        result.Exception.Should().BeOfType<PublisherSentFailedException>();
        await _connectionFactory.Received(1).CreateProducerAsync("headless-bus-TestTopic");
    }

    [Fact]
    public async Task should_wrap_exception_from_create_producer()
    {
        // given
        await using var transport = new PulsarTransport(_logger, _connectionFactory);
        var message = _CreateTransportMessage("msg-123", "TestTopic");

        _connectionFactory
            .CreateProducerAsync(Arg.Any<string>())
            .ThrowsAsync(new InvalidOperationException("Connection failed"));

        // when
        var result = await transport.SendAsync(message, AbortToken);

        // then
        result.Succeeded.Should().BeFalse();
        result.Exception.Should().BeOfType<PublisherSentFailedException>();
        result.Exception!.InnerException.Should().BeOfType<InvalidOperationException>();
    }

    [Fact]
    public async Task should_dispose_without_error()
    {
        // given
        await using var transport = new PulsarTransport(_logger, _connectionFactory);

        // when
        var act = async () => await transport.DisposeAsync();

        // then
        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task should_use_message_name_as_topic()
    {
        // given
        await using var transport = new PulsarTransport(_logger, _connectionFactory);
        var message = _CreateTransportMessage("msg-123", "orders.created");

        _connectionFactory
            .CreateProducerAsync(Arg.Any<string>())
            .ThrowsAsync(new InvalidOperationException("Expected"));

        // when
        await transport.SendAsync(message, AbortToken);

        // then
        await _connectionFactory.Received(1).CreateProducerAsync("headless-bus-orders.created");
    }

    [Theory]
    [InlineData(MessageLane.Bus, "orders.created", "headless-bus-orders.created")]
    [InlineData(MessageLane.Queue, "orders.created", "headless-queue-orders.created")]
    [InlineData(
        MessageLane.Bus,
        "persistent://tenant/namespace/orders.created",
        "persistent://tenant/namespace/headless-bus-orders.created"
    )]
    [InlineData(
        MessageLane.Queue,
        "persistent://tenant/namespace/orders.created",
        "persistent://tenant/namespace/headless-queue-orders.created"
    )]
    public void should_lane_qualify_physical_topic(MessageLane lane, string logicalName, string expected)
    {
        PulsarPhysicalAddress.Topic(lane, logicalName).Should().Be(expected);
    }

    [Fact]
    public void should_disambiguate_subscription_names_changed_by_normalization()
    {
        PulsarPhysicalAddress
            .Subscription(MessageLane.Bus, "sales.east")
            .Should()
            .NotBe(PulsarPhysicalAddress.Subscription(MessageLane.Bus, "sales_east"));
    }

    [Fact]
    public async Task should_propagate_cancellation()
    {
        // given
        await using var transport = new PulsarTransport(_logger, _connectionFactory);
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        // when
        var act = async () => await transport.SendAsync(_CreateTransportMessage("msg-123", "TestTopic"), cts.Token);

        // then
        await act.Should().ThrowAsync<OperationCanceledException>();
        _connectionFactory
            .ReceivedCalls()
            .Should()
            .NotContain(call => call.GetMethodInfo().Name == nameof(IConnectionFactory.CreateProducerAsync));
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
