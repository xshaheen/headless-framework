// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Messaging;
using Headless.Messaging.Internal;
using Headless.Messaging.RabbitMq;
using Headless.Messaging.Transport;
using Headless.Testing.Tests;
using Microsoft.Extensions.DependencyInjection;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using Headers = Headless.Messaging.Headers;

namespace Tests;

public sealed class RabbitMqBasicConsumerEnvelopeTests : TestBase
{
    private const string _RawName = "runs.result";
    private const string _StrictName = "orders.placed";
    private const string _RawRoutingKey = "queue." + _RawName;
    private const string _StrictRoutingKey = "queue." + _StrictName;
    private const ulong _DeliveryTag = 7ul;

    private static readonly DateTimeOffset _ProducedAt = new(2026, 10, 9, 12, 0, 0, TimeSpan.Zero);

    private readonly IChannel _channel = Substitute.For<IChannel>();
    private readonly IServiceProvider _serviceProvider = new ServiceCollection().BuildServiceProvider();
    private readonly List<LogMessageEventArgs> _loggedEvents = [];

    private readonly TransportEnvelopeGate _gate = new([
        _Route(_RawName, "2", raw: true),
        _Route(_StrictName, "1", raw: false),
    ]);

    private readonly Dictionary<string, string> _arrivalNames = new(StringComparer.Ordinal)
    {
        [_RawRoutingKey] = _RawName,
        [_StrictRoutingKey] = _StrictName,
    };

    public RabbitMqBasicConsumerEnvelopeTests()
    {
        _channel.IsOpen.Returns(true);
    }

    protected override async ValueTask DisposeAsyncCore()
    {
        await _channel.DisposeAsync();
        await base.DisposeAsyncCore();
    }

    [Fact]
    public async Task should_complete_a_headerless_delivery_when_it_arrives_on_a_raw_routing_key()
    {
        // given
        TransportMessage? received = null;
        using var consumer = _CreateConsumer(message => received = message);
        var properties = _Properties(messageId: "producer-42", timestamp: _ProducedAt);

        // when
        await _DeliverAsync(consumer, _RawRoutingKey, properties);

        // then
        received.Should().NotBeNull();
        var headers = received!.Value.Headers;
        headers[Headers.MessageName].Should().Be(_RawName);
        headers[Headers.MessageId].Should().Be("producer-42");
        headers[Headers.ContractVersion].Should().Be("2");
        headers[Headers.CorrelationId].Should().Be("producer-42");
        headers[Headers.SentTime].Should().Be(_ProducedAt.ToString("O", CultureInfo.InvariantCulture));
        headers[Headers.TransportAddress].Should().Be(_RawRoutingKey);
        await _channel
            .DidNotReceive()
            .BasicRejectAsync(Arg.Any<ulong>(), Arg.Any<bool>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task should_terminally_reject_a_raw_delivery_when_it_carries_no_message_id()
    {
        // given
        var callbackInvoked = false;
        using var consumer = _CreateConsumer(_ => callbackInvoked = true);

        // when
        await _DeliverAsync(consumer, _RawRoutingKey, _Properties(messageId: null, timestamp: _ProducedAt));

        // then
        callbackInvoked.Should().BeFalse();
        await _channel.Received(1).BasicRejectAsync(_DeliveryTag, false, Arg.Any<CancellationToken>());
        _loggedEvents.Should().ContainSingle(e => e.LogType == MqLogType.ConsumeError);
    }

    [Fact]
    public async Task should_terminally_reject_a_headerless_delivery_when_its_route_is_not_raw()
    {
        // given
        var callbackInvoked = false;
        using var consumer = _CreateConsumer(_ => callbackInvoked = true);

        // when
        await _DeliverAsync(consumer, _StrictRoutingKey, _Properties(messageId: "producer-42", timestamp: null));

        // then
        callbackInvoked.Should().BeFalse();
        await _channel.Received(1).BasicRejectAsync(_DeliveryTag, false, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task should_terminally_reject_a_raw_delivery_when_its_name_header_names_another_contract()
    {
        // given
        var callbackInvoked = false;
        using var consumer = _CreateConsumer(_ => callbackInvoked = true);
        var properties = _Properties(
            messageId: "producer-42",
            timestamp: null,
            new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                [Headers.MessageId] = "m-1"u8.ToArray(),
                [Headers.MessageName] = Encoding.UTF8.GetBytes(_StrictName),
            }
        );

        // when
        await _DeliverAsync(consumer, _RawRoutingKey, properties);

        // then
        callbackInvoked.Should().BeFalse();
        await _channel.Received(1).BasicRejectAsync(_DeliveryTag, false, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task should_route_by_header_when_the_routing_key_is_not_a_subscribed_one()
    {
        // given
        TransportMessage? received = null;
        using var consumer = _CreateConsumer(message => received = message);
        var properties = _Properties(
            messageId: null,
            timestamp: null,
            new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                [Headers.MessageId] = "m-1"u8.ToArray(),
                [Headers.MessageName] = Encoding.UTF8.GetBytes(_StrictName),
            }
        );

        // when
        await _DeliverAsync(consumer, "queue.unknown", properties);

        // then
        received.Should().NotBeNull();
        received!.Value.Headers[Headers.MessageName].Should().Be(_StrictName);
        received.Value.Headers[Headers.MessageId].Should().Be("m-1");
    }

    [Fact]
    public async Task should_complete_a_raw_delivery_from_custom_headers_when_the_builder_supplies_the_identity()
    {
        // given
        TransportMessage? received = null;
        static List<KeyValuePair<string, string>> builder(BasicDeliverEventArgs _, IServiceProvider __) =>
            [new(Headers.MessageId, "from-builder")];
        using var consumer = _CreateConsumer(message => received = message, builder);

        // when
        await _DeliverAsync(consumer, _RawRoutingKey, _Properties(messageId: null, timestamp: null));

        // then
        received.Should().NotBeNull();
        received!.Value.Headers[Headers.MessageId].Should().Be("from-builder");
        received.Value.Headers[Headers.MessageName].Should().Be(_RawName);
    }

    [Fact]
    public void should_read_the_producer_message_id_and_timestamp_when_the_producer_set_them()
    {
        // when
        var facts = RabbitMqBasicConsumer.ReadNativeFacts(_Properties("producer-42", _ProducedAt));

        // then
        facts.ProducerMessageId.Should().Be("producer-42");
        facts.StoredAt.Should().Be(_ProducedAt);
        facts.PositionId.Should().BeNull("a delivery tag changes on redelivery and cannot identify a message");
    }

    [Fact]
    public void should_read_no_identity_when_the_producer_set_no_message_id_or_timestamp()
    {
        // when
        var facts = RabbitMqBasicConsumer.ReadNativeFacts(_Properties(messageId: null, timestamp: null));

        // then
        facts.ProducerMessageId.Should().BeNull();
        facts.PositionId.Should().BeNull();
        facts.StoredAt.Should().BeNull();
    }

    private RabbitMqBasicConsumer _CreateConsumer(
        Action<TransportMessage> onMessage,
        Func<BasicDeliverEventArgs, IServiceProvider, List<KeyValuePair<string, string>>>? customHeadersBuilder = null
    ) =>
        new(
            _channel,
            0,
            (message, _) =>
            {
                onMessage(message);
                return Task.CompletedTask;
            },
            _loggedEvents.Add,
            customHeadersBuilder,
            _serviceProvider,
            onBrokerCancelled: null,
            _gate,
            MessageLane.Queue,
            _arrivalNames
        );

    private static Task _DeliverAsync(
        RabbitMqBasicConsumer consumer,
        string routingKey,
        IReadOnlyBasicProperties properties
    ) =>
        consumer.HandleBasicDeliverAsync(
            "consumer-tag",
            _DeliveryTag,
            false,
            "test-exchange.queue",
            routingKey,
            properties,
            """{"value":1}"""u8.ToArray(),
            AbortToken
        );

    private static IReadOnlyBasicProperties _Properties(
        string? messageId,
        DateTimeOffset? timestamp,
        IDictionary<string, object?>? headers = null
    )
    {
        var properties = Substitute.For<IReadOnlyBasicProperties>();
        properties.Headers.Returns(headers);
        properties.IsMessageIdPresent().Returns(messageId is not null);
        properties.MessageId.Returns(messageId);
        properties.IsTimestampPresent().Returns(timestamp is not null);

        if (timestamp is { } value)
        {
            properties.Timestamp.Returns(new AmqpTimestamp(value.ToUnixTimeSeconds()));
        }

        return properties;
    }

    private static MessageMetadata _Route(string name, string version, bool raw) =>
        new(
            new MessageRouteKey(typeof(object), name, MessageLane.Queue),
            typeof(object),
            version,
            CorrelationSelector: null,
            new Dictionary<Type, object>(),
            RawPayload: raw
        );
}
