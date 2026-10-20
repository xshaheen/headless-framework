// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Reflection;
using Azure.Messaging.ServiceBus;
using Headless.Messaging;
using Headless.Messaging.AzureServiceBus;
using Headless.Messaging.Internal;
using Headless.Messaging.Transport;
using Headless.Testing.Tests;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Tests;

/// <summary>
/// A delivery a foreign producer published with the Azure client, without the Headless envelope headers, reaches a
/// contract declared with <c>RawPayload()</c> by the address it arrived on, with an identity every redelivery keeps.
/// </summary>
public sealed class AzureServiceBusRawPayloadTests : TestBase
{
    private const string _ConnectionString =
        "Endpoint=sb://mynamespace.servicebus.windows.net/;SharedAccessKeyName=myPolicy;SharedAccessKey=myKey";

    private const string _RawName = "runs.result";
    private const string _StrictName = "orders.placed";
    private const string _BusEntityPath = "headless/Subscriptions/runs";
    private const long _SequenceNumber = 42;

    private static readonly DateTimeOffset _EnqueuedTime = new(2026, 10, 9, 12, 0, 0, TimeSpan.Zero);

    private readonly ServiceBusClient _serviceBusClient = Substitute.For<ServiceBusClient>();
    private readonly IAzureServiceBusClientPool _pool = Substitute.For<IAzureServiceBusClientPool>();
    private readonly List<TransportMessage> _received = [];

    public AzureServiceBusRawPayloadTests()
    {
        _serviceBusClient
            .CreateProcessor(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<ServiceBusProcessorOptions>())
            .Returns(_ => Substitute.For<ServiceBusProcessor>());
        _serviceBusClient
            .CreateProcessor(Arg.Any<string>(), Arg.Any<ServiceBusProcessorOptions>())
            .Returns(_ => Substitute.For<ServiceBusProcessor>());
        _serviceBusClient
            .CreateSessionProcessor(Arg.Any<string>(), Arg.Any<ServiceBusSessionProcessorOptions>())
            .Returns(_ => Substitute.For<ServiceBusSessionProcessor>());
        _pool.GetClient().Returns(_serviceBusClient);
    }

    [Fact]
    public async Task should_complete_a_headerless_bus_delivery_whose_subject_names_a_raw_contract()
    {
        // given
        await using var client = await _SubscribeAsync(MessageLane.Bus, [_RawName, _StrictName]);
        var message = _Message(subject: _RawName, messageId: "producer-7");

        // when
        var receiver = await _ProcessAsync(client, message, _BusEntityPath);

        // then
        _received.Should().ContainSingle();
        var headers = _received[0].Headers;
        headers[Headers.MessageName].Should().Be(_RawName);
        headers[Headers.MessageId].Should().Be("producer-7");
        headers[Headers.ContractVersion].Should().Be("2");
        headers[Headers.CorrelationId].Should().Be("producer-7");
        headers[Headers.SentTime].Should().Be(_EnqueuedTime.ToString("O", CultureInfo.InvariantCulture));
        headers[Headers.TransportAddress].Should().Be(_BusEntityPath);
        _received[0].Body.ToArray().Should().Equal("{\"ok\":true}"u8.ToArray());
        await _AssertNotDeadLetteredAsync(receiver);
    }

    [Fact]
    public async Task should_give_every_redelivery_of_a_headerless_queue_delivery_the_same_position_id()
    {
        // given: the producer set no MessageId, so the identity comes from the queue's sequence number
        await using var client = await _SubscribeAsync(MessageLane.Queue, [_RawName]);
        var first = _Message(subject: null, messageId: null, deliveryCount: 1);
        var redelivery = _Message(subject: null, messageId: null, deliveryCount: 2);

        // when
        await _ProcessAsync(client, first, _RawName);
        await _ProcessAsync(client, redelivery, _RawName);

        // then
        var expectedId = $"asb:{_RawName}:{_SequenceNumber}:{_EnqueuedTime.UtcTicks}";
        _received.Select(m => m.Headers[Headers.MessageId]).Should().Equal(expectedId, expectedId);
        _received.Should().OnlyContain(m => m.Headers[Headers.MessageName] == _RawName);
    }

    [Fact]
    public async Task should_dead_letter_a_headerless_delivery_when_its_queue_is_a_strict_route()
    {
        // given
        await using var client = await _SubscribeAsync(MessageLane.Queue, [_StrictName]);
        var message = _Message(subject: null, messageId: "producer-7");

        // when
        var receiver = await _ProcessAsync(client, message, _StrictName);

        // then
        _received.Should().BeEmpty();
        await _AssertDeadLetteredAsync(receiver, message);
    }

    [Fact]
    public async Task should_dead_letter_a_headerless_delivery_when_its_subject_is_not_a_subscribed_name()
    {
        // given: the subject names the raw contract, but this client did not subscribe it
        await using var client = await _SubscribeAsync(MessageLane.Bus, [_StrictName]);
        var message = _Message(subject: _RawName, messageId: "producer-7");

        // when
        var receiver = await _ProcessAsync(client, message, _BusEntityPath);

        // then
        _received.Should().BeEmpty();
        await _AssertDeadLetteredAsync(receiver, message);
    }

    [Fact]
    public async Task should_dead_letter_a_raw_delivery_when_its_header_names_another_contract()
    {
        // given
        await using var client = await _SubscribeAsync(MessageLane.Bus, [_RawName, _StrictName]);
        var message = _Message(
            subject: _RawName,
            messageId: "producer-7",
            properties: new(StringComparer.Ordinal) { [Headers.MessageName] = _StrictName }
        );

        // when
        var receiver = await _ProcessAsync(client, message, _BusEntityPath);

        // then
        _received.Should().BeEmpty();
        await _AssertDeadLetteredAsync(receiver, message);
    }

    [Fact]
    public async Task should_route_by_header_when_a_headless_delivery_arrives_on_a_strict_route()
    {
        // given
        await using var client = await _SubscribeAsync(MessageLane.Bus, [_StrictName]);
        var message = _Message(
            subject: _StrictName,
            messageId: "m-1",
            properties: new(StringComparer.Ordinal) { [Headers.MessageId] = "m-1", [Headers.MessageName] = _StrictName }
        );

        // when
        await _ProcessAsync(client, message, _BusEntityPath);

        // then
        _received.Should().ContainSingle();
        _received[0].Headers.Should().NotContainKey(Headers.ContractVersion, "the gate completes only raw routes");
    }

    [Fact]
    public async Task should_let_the_custom_headers_builder_overwrite_a_wire_header()
    {
        // given
        await using var client = await _SubscribeAsync(
            MessageLane.Bus,
            [_RawName],
            options => options.CustomHeadersBuilder = static (_, _) => [new(Headers.MessageId, "translated-id")]
        );
        var message = _Message(
            subject: _RawName,
            messageId: "producer-7",
            properties: new(StringComparer.Ordinal) { [Headers.MessageId] = "foreign-id" }
        );

        // when
        await _ProcessAsync(client, message, _BusEntityPath);

        // then
        _received.Should().ContainSingle();
        _received[0].Headers[Headers.MessageId].Should().Be("translated-id");
    }

    [Fact]
    public async Task should_abandon_for_redelivery_when_the_custom_headers_builder_throws_on_a_session_processor()
    {
        // given
        await using var client = await _SubscribeAsync(
            MessageLane.Bus,
            [_RawName],
            options => options.CustomHeadersBuilder = static (_, _) => throw new InvalidOperationException("bad")
        );
        var message = _Message(subject: _RawName, messageId: "producer-7");
        var receiver = Substitute.For<ServiceBusSessionReceiver>();
        receiver.EntityPath.Returns(_BusEntityPath);

        // when
        await _InvokeAsync(
            client,
            "_ServiceBusProcessor_ProcessSessionMessageAsync",
            new ProcessSessionMessageEventArgs(message, receiver, AbortToken)
        );

        // then
        _received.Should().BeEmpty();
        await receiver
            .Received(1)
            .AbandonMessageAsync(message, Arg.Any<IDictionary<string, object>>(), Arg.Any<CancellationToken>());
        await receiver
            .DidNotReceive()
            .DeadLetterMessageAsync(
                Arg.Any<ServiceBusReceivedMessage>(),
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public void should_read_the_producer_message_id_position_and_enqueue_time_from_a_delivery()
    {
        // given
        var message = _Message(subject: _RawName, messageId: "producer-7");

        // when
        var facts = AzureServiceBusConsumerClient.ReadNativeFacts(message, _BusEntityPath);

        // then
        facts.ProducerMessageId.Should().Be("producer-7");
        facts.PositionId.Should().Be($"asb:{_BusEntityPath}:{_SequenceNumber}:{_EnqueuedTime.UtcTicks}");
        facts.StoredAt.Should().Be(_EnqueuedTime);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("  ")]
    public void should_read_no_producer_message_id_when_the_delivery_has_a_blank_one(string? messageId)
    {
        // given
        var message = _Message(subject: _RawName, messageId: messageId);

        // when
        var facts = AzureServiceBusConsumerClient.ReadNativeFacts(message, _RawName);

        // then
        facts.ProducerMessageId.Should().BeNull();
        facts.PositionId.Should().Be($"asb:{_RawName}:{_SequenceNumber}:{_EnqueuedTime.UtcTicks}");
    }

    [Fact]
    public void should_hash_the_position_id_when_the_entity_path_makes_it_too_long()
    {
        // given
        var entityPath = new string('q', 250);
        var message = _Message(subject: null, messageId: null);

        // when
        var first = AzureServiceBusConsumerClient.ReadNativeFacts(message, entityPath);
        var second = AzureServiceBusConsumerClient.ReadNativeFacts(message, entityPath);

        // then
        first.PositionId.Should().StartWith("asb:sha256:");
        first.PositionId!.Length.Should().BeLessThanOrEqualTo(MessageOptions.MessageIdMaxLength);
        second.PositionId.Should().Be(first.PositionId);
    }

    private async Task<AzureServiceBusConsumerClient> _SubscribeAsync(
        MessageLane lane,
        string[] names,
        Action<AzureServiceBusMessagingOptions>? configure = null
    )
    {
        // AutoProvision off keeps the administration client out: these tests drive the receive path only.
        var options = new AzureServiceBusMessagingOptions
        {
            ConnectionString = _ConnectionString,
            AutoProvision = false,
        };
        configure?.Invoke(options);

        var services = new ServiceCollection();
        services.AddSingleton(
            new TransportEnvelopeGate([_Route(_RawName, "2", lane, raw: true), _Route(_StrictName, "1", lane)])
        );

        var client = new AzureServiceBusConsumerClient(
            NullLogger.Instance,
            "runs",
            1,
            Options.Create(options),
            services.BuildServiceProvider(),
            _pool,
            lane
        );
        client.AttachCallbacks(
            (message, _) =>
            {
                _received.Add(message);
                return Task.CompletedTask;
            },
            _ => { }
        );

        await client.SubscribeAsync(names, AbortToken);

        return client;
    }

    private static async Task<ServiceBusReceiver> _ProcessAsync(
        AzureServiceBusConsumerClient client,
        ServiceBusReceivedMessage message,
        string entityPath
    )
    {
        var receiver = Substitute.For<ServiceBusReceiver>();
        receiver.EntityPath.Returns(entityPath);

        await _InvokeAsync(
            client,
            "_ServiceBusProcessor_ProcessMessageAsync",
            new ProcessMessageEventArgs(message, receiver, AbortToken)
        );

        return receiver;
    }

    private static Task _InvokeAsync<TArgs>(AzureServiceBusConsumerClient client, string handler, TArgs args)
    {
        var method = typeof(AzureServiceBusConsumerClient).GetMethod(
            handler,
            BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.DeclaredOnly,
            null,
            [typeof(TArgs)],
            null
        )!;

        return (Task)method.Invoke(client, [args])!;
    }

    private static async Task _AssertDeadLetteredAsync(ServiceBusReceiver receiver, ServiceBusReceivedMessage message)
    {
        await receiver
            .Received(1)
            .DeadLetterMessageAsync(
                message,
                "MalformedEnvelope",
                nameof(InvalidDataException),
                Arg.Any<CancellationToken>()
            );
    }

    private static async Task _AssertNotDeadLetteredAsync(ServiceBusReceiver receiver)
    {
        await receiver
            .DidNotReceive()
            .DeadLetterMessageAsync(
                Arg.Any<ServiceBusReceivedMessage>(),
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<CancellationToken>()
            );
    }

    private static ServiceBusReceivedMessage _Message(
        string? subject,
        string? messageId,
        int deliveryCount = 1,
        Dictionary<string, object>? properties = null
    ) =>
        ServiceBusModelFactory.ServiceBusReceivedMessage(
            body: new BinaryData("{\"ok\":true}"u8.ToArray()),
            messageId: messageId,
            subject: subject,
            properties: properties ?? new Dictionary<string, object>(StringComparer.Ordinal),
            lockTokenGuid: Guid.NewGuid(),
            deliveryCount: deliveryCount,
            sequenceNumber: _SequenceNumber,
            enqueuedTime: _EnqueuedTime
        );

    private static MessageMetadata _Route(string name, string version, MessageLane lane, bool raw = false) =>
        new(
            new MessageRouteKey(typeof(object), name, lane),
            typeof(object),
            version,
            CorrelationSelector: null,
            new Dictionary<Type, object>(),
            RawPayload: raw
        );
}
