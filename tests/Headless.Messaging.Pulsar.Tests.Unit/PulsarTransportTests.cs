// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Buffers;
using DotPulsar;
using DotPulsar.Abstractions;
using DotPulsar.Exceptions;
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
    public async Task should_return_failed_result_without_sending_when_sending_after_dispose()
    {
        var transport = new PulsarTransport(_logger, _connectionFactory);
        await transport.DisposeAsync();

        var result = await transport.SendAsync(_CreateTransportMessage("msg-123", "TestTopic"), AbortToken);

        result.Succeeded.Should().BeFalse();
        result.Exception.Should().BeOfType<ObjectDisposedException>();
        _connectionFactory.DidNotReceive().GetProducer(Arg.Any<string>());
    }

    [Fact]
    public async Task should_pass_affinity_to_native_producer_message_builder()
    {
        var producer = Substitute.For<IProducer<ReadOnlySequence<byte>>>();
        _connectionFactory.GetProducer("headless-bus-orders").Returns(producer);
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

        await producer
            .Received(1)
            .Send(
                Arg.Is<MessageMetadata>(metadata => metadata.Key == "order-42"),
                Arg.Any<ReadOnlySequence<byte>>(),
                Arg.Any<CancellationToken>()
            );
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
        _connectionFactory.DidNotReceive().GetProducer(Arg.Any<string>());
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

        _connectionFactory.GetProducer(Arg.Any<string>()).Throws(new InvalidOperationException("Expected"));

        // when
        var result = await transport.SendAsync(message, AbortToken);

        // then
        result.Succeeded.Should().BeFalse();
        result.Exception.Should().BeOfType<PublisherSentFailedException>();
        _connectionFactory.Received(1).GetProducer("headless-bus-TestTopic");
    }

    [Fact]
    public async Task should_wrap_exception_from_create_producer()
    {
        // given
        await using var transport = new PulsarTransport(_logger, _connectionFactory);
        var message = _CreateTransportMessage("msg-123", "TestTopic");

        _connectionFactory.GetProducer(Arg.Any<string>()).Throws(new InvalidOperationException("Connection failed"));

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

        _connectionFactory.GetProducer(Arg.Any<string>()).Throws(new InvalidOperationException("Expected"));

        // when
        await transport.SendAsync(message, AbortToken);

        // then
        _connectionFactory.Received(1).GetProducer("headless-bus-orders.created");
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
            .NotContain(call => call.GetMethodInfo().Name == nameof(IConnectionFactory.GetProducer));
    }

    [Fact]
    public async Task should_send_the_body_without_copying_it()
    {
        // given
        var producer = Substitute.For<IProducer<ReadOnlySequence<byte>>>();
        _connectionFactory.GetProducer(Arg.Any<string>()).Returns(producer);
        ReadOnlySequence<byte> sent = default;
        producer
            .Send(
                Arg.Any<MessageMetadata>(),
                Arg.Do<ReadOnlySequence<byte>>(body => sent = body),
                Arg.Any<CancellationToken>()
            )
            .Returns(ValueTask.FromResult(new MessageId(1, 2, -1, -1)));
        await using var transport = new PulsarTransport(_logger, _connectionFactory);
        var source = "xxbodyyy"u8.ToArray();
        var message = new TransportMessage(
            new Dictionary<string, string?>(StringComparer.Ordinal)
            {
                [MessagingHeaders.MessageId] = "msg-123",
                [MessagingHeaders.MessageName] = "orders",
            },
            new ReadOnlyMemory<byte>(source, 2, 4)
        );

        // when
        var result = await transport.SendAsync(message, AbortToken);

        // then
        result.Succeeded.Should().BeTrue();
        sent.ToArray().Should().Equal("body"u8.ToArray());
        System.Runtime.InteropServices.MemoryMarshal.TryGetArray(sent.First, out var segment).Should().BeTrue();
        segment.Array.Should().BeSameAs(source);
    }

    [Fact]
    public async Task should_send_every_header_with_a_value_as_a_property()
    {
        // given
        var producer = Substitute.For<IProducer<ReadOnlySequence<byte>>>();
        _connectionFactory.GetProducer(Arg.Any<string>()).Returns(producer);
        MessageMetadata? sent = null;
        producer
            .Send(
                Arg.Do<MessageMetadata>(metadata => sent = metadata),
                Arg.Any<ReadOnlySequence<byte>>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(ValueTask.FromResult(new MessageId(1, 2, -1, -1)));
        await using var transport = new PulsarTransport(_logger, _connectionFactory);
        var message = new TransportMessage(
            new Dictionary<string, string?>(StringComparer.Ordinal)
            {
                [MessagingHeaders.MessageId] = "msg-123",
                [MessagingHeaders.MessageName] = "orders",
                ["custom"] = null,
            },
            "body"u8.ToArray()
        );

        // when
        await transport.SendAsync(message, AbortToken);

        // then: a Pulsar property always has a value
        sent!["headless-msg-id"].Should().Be("msg-123");
        sent[MessagingHeaders.MessageName].Should().Be("orders");
        sent["custom"].Should().BeNull();
        sent.Key.Should().BeNullOrEmpty("a message without an affinity key goes out without one");
    }

    [Fact]
    public async Task should_report_the_broker_unreachable_when_a_send_times_out()
    {
        // given: the client keeps a send pending while it reconnects
        var producer = Substitute.For<IProducer<ReadOnlySequence<byte>>>();
        _connectionFactory.GetProducer(Arg.Any<string>()).Returns(producer);
        _connectionFactory.SendTimeout.Returns(TimeSpan.FromMilliseconds(50));
        producer
            .Send(Arg.Any<MessageMetadata>(), Arg.Any<ReadOnlySequence<byte>>(), Arg.Any<CancellationToken>())
            .Returns(call => new ValueTask<MessageId>(_NeverAsync(call.Arg<CancellationToken>())));
        await using var transport = new PulsarTransport(_logger, _connectionFactory);

        // when
        var result = await transport.SendAsync(_CreateTransportMessage("msg-123", "orders"), AbortToken);

        // then
        result.Succeeded.Should().BeFalse();
        result.IsBrokerUnreachable.Should().BeTrue();
        result.Exception!.InnerException.Should().BeOfType<TimeoutException>();
    }

    [Fact]
    public async Task should_report_the_broker_unreachable_when_the_producer_is_faulted()
    {
        // given: a producer DotPulsar faulted earlier, for example on an unresolvable host
        var producer = Substitute.For<IProducer<ReadOnlySequence<byte>>>();
        _connectionFactory.GetProducer(Arg.Any<string>()).Returns(producer);
        producer
            .Send(Arg.Any<MessageMetadata>(), Arg.Any<ReadOnlySequence<byte>>(), Arg.Any<CancellationToken>())
            .Returns(ValueTask.FromException<MessageId>(new ProducerFaultedException()));
        await using var transport = new PulsarTransport(_logger, _connectionFactory);

        // when
        var result = await transport.SendAsync(_CreateTransportMessage("msg-123", "orders"), AbortToken);

        // then
        result.Succeeded.Should().BeFalse();
        result.IsBrokerUnreachable.Should().BeTrue();
    }

    [Fact]
    public async Task should_fail_at_once_without_counting_the_broker_unreachable_when_the_message_is_too_large()
    {
        // given: DotPulsar faults the producer on a message larger than the broker accepts, and the send never returns
        var refusals = new PulsarSizeRefusals(TimeProvider.System);
        _FaultingProducer(onSend: () => refusals.Record(new TooLargeMessageException(9 + 120, 100)));
        _connectionFactory.SizeRefusals.Returns(refusals);
        await using var transport = new PulsarTransport(_logger, _connectionFactory);

        // when
        var result = await transport
            .SendAsync(_CreateTransportMessage("msg-123", "orders"), AbortToken)
            .WaitAsync(TimeSpan.FromSeconds(5), AbortToken);

        // then
        result.Succeeded.Should().BeFalse();
        result.IsBrokerUnreachable.Should().BeFalse("a message the broker cannot take is this message's problem");
        result
            .Exception!.InnerException.Should()
            .BeOfType<ProducerFaultedException>()
            .Which.InnerException.Should()
            .BeOfType<TooLargeMessageException>();
    }

    [Fact]
    public async Task should_fail_at_once_and_count_the_broker_unreachable_when_the_producer_faults_for_another_reason()
    {
        // given: a fault with no size refusal behind it, such as an authentication failure or an unresolvable host
        _FaultingProducer(onSend: static () => { });
        _connectionFactory.SizeRefusals.Returns(new PulsarSizeRefusals(TimeProvider.System));
        await using var transport = new PulsarTransport(_logger, _connectionFactory);

        // when
        var result = await transport
            .SendAsync(_CreateTransportMessage("msg-123", "orders"), AbortToken)
            .WaitAsync(TimeSpan.FromSeconds(5), AbortToken);

        // then
        result.Succeeded.Should().BeFalse();
        result.IsBrokerUnreachable.Should().BeTrue("a fault not caused by the message may be an outage");
    }

    private void _FaultingProducer(Action onSend)
    {
        var producer = Substitute.For<IProducer<ReadOnlySequence<byte>>>();
        _connectionFactory.GetProducer(Arg.Any<string>()).Returns(producer);
        _connectionFactory.SendTimeout.Returns(TimeSpan.FromMinutes(5));
        producer
            .Send(Arg.Any<MessageMetadata>(), Arg.Any<ReadOnlySequence<byte>>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                onSend();
                return new ValueTask<MessageId>(_NeverAsync(call.Arg<CancellationToken>()));
            });
        producer
            .State.OnStateChangeTo(ProducerState.Faulted, Arg.Any<CancellationToken>())
            .Returns(ValueTask.FromResult(ProducerState.Faulted));
    }

    private static async Task<MessageId> _NeverAsync(CancellationToken cancellationToken)
    {
        await Task.Delay(Timeout.Infinite, cancellationToken);
        throw new InvalidOperationException("unreachable");
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
