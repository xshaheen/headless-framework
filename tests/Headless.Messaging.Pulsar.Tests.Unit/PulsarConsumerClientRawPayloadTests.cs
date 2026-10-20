// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Collections.Concurrent;
using System.Reflection;
using Headless.Messaging;
using Headless.Messaging.Internal;
using Headless.Messaging.Pulsar;
using Headless.Messaging.Transport;
using Headless.Testing.Tests;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Pulsar.Client.Api;
using Pulsar.Client.Common;
using Pulsar.Client.Internal;
using MessagingHeaders = Headless.Messaging.Headers;

namespace Tests;

public sealed class PulsarConsumerClientRawPayloadTests : TestBase
{
    private const string _RawName = "runs.result";
    private const string _StrictName = "orders.placed";
    private const long _PublishTimeMs = 1_791_547_200_000;

    private static readonly string _RawTopic =
        $"persistent://public/default/{PulsarPhysicalAddress.Topic(MessageLane.Queue, _RawName)}";

    private static readonly string _StrictTopic =
        $"persistent://public/default/{PulsarPhysicalAddress.Topic(MessageLane.Queue, _StrictName)}";

    [Fact]
    public async Task should_name_the_arrival_message_when_the_topic_was_subscribed_in_any_reported_form()
    {
        // given
        await using var client = _CreateClient();
        var shortTopic = PulsarPhysicalAddress.Topic(MessageLane.Queue, _RawName);

        // when
        client.IndexArrivalNames([_RawName, _StrictName]);

        // then
        client.ArrivalName(shortTopic).Should().Be(_RawName);
        client.ArrivalName(_RawTopic).Should().Be(_RawName);
        client.ArrivalName($"{_RawTopic}-partition-3").Should().Be(_RawName);
        client.ArrivalName(_StrictTopic).Should().Be(_StrictName);
        client.ArrivalName("persistent://public/default/headless-queue-other").Should().BeNull();
        client.ArrivalName("persistent://other/default/" + shortTopic).Should().BeNull();
    }

    [Fact]
    public async Task should_name_no_arrival_message_when_two_names_resolve_to_one_topic()
    {
        // given
        await using var client = _CreateClient();

        // when
        client.IndexArrivalNames([_RawName, "public/default/" + _RawName]);

        // then
        client.ArrivalName(_RawTopic).Should().BeNull();
    }

    [Fact]
    public async Task should_name_no_arrival_message_when_the_lane_has_no_raw_routes()
    {
        // given
        await using var client = _CreateClient(lane: MessageLane.Bus);

        // when
        client.IndexArrivalNames([_RawName]);

        // then
        client.ArrivalName(PulsarPhysicalAddress.Topic(MessageLane.Bus, _RawName)).Should().BeNull();
    }

    [Theory]
    [InlineData("headless-queue-runs.result", "persistent://public/default/headless-queue-runs.result")]
    [InlineData("tenant/ns/headless-queue-x", "persistent://tenant/ns/headless-queue-x")]
    [InlineData("persistent://public/default/x-partition-12", "persistent://public/default/x")]
    [InlineData("non-persistent://t/n/x", "non-persistent://t/n/x")]
    public void should_normalize_a_topic_to_its_complete_partition_free_name(string topic, string expected)
    {
        // when
        var normalized = PulsarConsumerClient.NormalizeTopic(topic);

        // then
        normalized.Should().Be(expected);
    }

    [Fact]
    public void should_identify_a_single_message_by_its_topic_ledger_and_entry()
    {
        // given
        var message = _Message(_RawTopic, new Dictionary<string, string>(StringComparer.Ordinal));

        // when
        var facts = PulsarConsumerClient.NativeFacts(message);

        // then
        facts.ProducerMessageId.Should().BeNull();
        facts.PositionId.Should().Be($"pulsar:{_RawTopic}:11:22:-1:-1");
        facts.StoredAt.Should().Be(DateTimeOffset.FromUnixTimeMilliseconds(_PublishTimeMs));
    }

    [Fact]
    public void should_identify_a_batched_partition_message_by_its_partition_and_batch_index()
    {
        // given
        var message = _Message(
            $"{_RawTopic}-partition-2",
            new Dictionary<string, string>(StringComparer.Ordinal),
            partition: 2,
            batchIndex: 4
        );

        // when
        var facts = PulsarConsumerClient.NativeFacts(message);

        // then
        facts.PositionId.Should().Be($"pulsar:{_RawTopic}:11:22:2:4");
    }

    [Fact]
    public void should_give_no_stored_time_when_the_message_has_no_publish_time()
    {
        // given
        var message = _Message(_RawTopic, new Dictionary<string, string>(StringComparer.Ordinal), publishTime: 0);

        // when
        var facts = PulsarConsumerClient.NativeFacts(message);

        // then
        facts.StoredAt.Should().BeNull();
    }

    [Fact]
    public async Task should_complete_a_headerless_delivery_when_its_topic_is_a_raw_contract()
    {
        // given
        var message = _Message($"{_RawTopic}-partition-0", new Dictionary<string, string>(StringComparer.Ordinal), 0);

        // when
        var outcome = await _ConsumeOneAsync(message);

        // then
        outcome.Acknowledged.Should().BeFalse();
        var headers = outcome.Delivered!.Value.Headers;
        var expectedId = $"pulsar:{_RawTopic}:11:22:0:-1";
        headers[MessagingHeaders.MessageName].Should().Be(_RawName);
        headers[MessagingHeaders.MessageId].Should().Be(expectedId);
        headers[MessagingHeaders.ContractVersion].Should().Be("2");
        headers[MessagingHeaders.CorrelationId].Should().Be(expectedId);
        headers[MessagingHeaders.SentTime]
            .Should()
            .Be(DateTimeOffset.FromUnixTimeMilliseconds(_PublishTimeMs).ToString("O", CultureInfo.InvariantCulture));
        headers[MessagingHeaders.TransportAddress].Should().Be($"{_RawTopic}-partition-0");
        outcome.Delivered.Value.Body.ToArray().Should().Equal("{}"u8.ToArray());
    }

    [Fact]
    public async Task should_acknowledge_a_headerless_delivery_without_a_callback_when_its_topic_is_a_strict_contract()
    {
        // given
        var message = _Message(_StrictTopic, new Dictionary<string, string>(StringComparer.Ordinal));

        // when
        var outcome = await _ConsumeOneAsync(message);

        // then
        outcome.Delivered.Should().BeNull();
        outcome.Acknowledged.Should().BeTrue();
    }

    [Fact]
    public async Task should_acknowledge_without_a_callback_when_a_header_names_another_contract_on_a_raw_topic()
    {
        // given
        var message = _Message(
            _RawTopic,
            new Dictionary<string, string>(StringComparer.Ordinal) { [MessagingHeaders.MessageName] = _StrictName }
        );

        // when
        var outcome = await _ConsumeOneAsync(message);

        // then
        outcome.Delivered.Should().BeNull();
        outcome.Acknowledged.Should().BeTrue();
    }

    [Fact]
    public async Task should_keep_the_wire_message_id_when_a_raw_delivery_carries_one()
    {
        // given
        var message = _Message(
            _RawTopic,
            new Dictionary<string, string>(StringComparer.Ordinal) { [MessagingHeaders.MessageId] = "producer-id" }
        );

        // when
        var outcome = await _ConsumeOneAsync(message);

        // then
        outcome.Delivered!.Value.Headers[MessagingHeaders.MessageId].Should().Be("producer-id");
    }

    [Fact]
    public async Task should_apply_custom_headers_over_wire_properties_before_the_envelope_check()
    {
        // given
        var message = _Message(
            _RawTopic,
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["ce-id"] = "cloud-event-1",
                [MessagingHeaders.MessageId] = "wire-id",
            }
        );

        // when
        var outcome = await _ConsumeOneAsync(
            message,
            (delivery, _) =>
                [
                    new(MessagingHeaders.MessageId, delivery.Properties["ce-id"]),
                    new(MessagingHeaders.TransportAddress, "builder-spoofed"),
                ]
        );

        // then
        var headers = outcome.Delivered!.Value.Headers;
        headers[MessagingHeaders.MessageId].Should().Be("cloud-event-1");
        headers[MessagingHeaders.TransportAddress].Should().Be(_RawTopic);
    }

    [Fact]
    public async Task should_negatively_acknowledge_without_a_callback_when_the_custom_headers_builder_throws()
    {
        // given
        var message = _Message(_RawTopic, new Dictionary<string, string>(StringComparer.Ordinal));

        // when
        var outcome = await _ConsumeOneAsync(message, (_, _) => throw new System.TimeoutException("lookup timed out"));

        // then: a builder failure may be transient, so the broker redelivers the message instead of losing it
        outcome.Delivered.Should().BeNull();
        outcome.NegativelyAcknowledged.Should().BeTrue();
        outcome.Acknowledged.Should().BeFalse();
        outcome
            .Logs.Should()
            .Contain(log => log.Reason!.Contains(nameof(System.TimeoutException), StringComparison.Ordinal));
    }

    [Fact]
    public void should_reject_a_custom_headers_builder_when_the_client_has_no_service_provider()
    {
        // given
        var options = Options.Create(
            new PulsarMessagingOptions { ServiceUrl = "pulsar://localhost:6650", CustomHeadersBuilder = (_, _) => [] }
        );

        // when
        var act = () =>
            new PulsarConsumerClient(
                options,
                client: null!,
                new ConsumerClientRequest("raw-test", concurrency: 0, MessageLane.Queue)
            );

        // then
        act.Should().Throw<InvalidOperationException>().WithMessage("*CustomHeadersBuilder*");
    }

    private static PulsarConsumerClient _CreateClient(
        MessageLane lane = MessageLane.Queue,
        Func<Message<byte[]>, IServiceProvider, List<KeyValuePair<string, string>>>? customHeadersBuilder = null
    )
    {
        var services = new ServiceCollection();
        services.AddSingleton(new TransportEnvelopeGate([_Route(_RawName, "2", raw: true), _Route(_StrictName, "1")]));
#pragma warning disable CA2000 // The provider holds only an instance registration, so leaving it undisposed releases nothing.
        var serviceProvider = services.BuildServiceProvider();
#pragma warning restore CA2000

        return new PulsarConsumerClient(
            Options.Create(
                new PulsarMessagingOptions
                {
                    ServiceUrl = "pulsar://localhost:6650",
                    CustomHeadersBuilder = customHeadersBuilder,
                }
            ),
            client: null!,
            new ConsumerClientRequest("raw-test", concurrency: 0, lane),
            serviceProvider: serviceProvider
        );
    }

    private async Task<ConsumeOutcome> _ConsumeOneAsync(
        Message<byte[]> delivery,
        Func<Message<byte[]>, IServiceProvider, List<KeyValuePair<string, string>>>? customHeadersBuilder = null
    )
    {
        await using var client = _CreateClient(customHeadersBuilder: customHeadersBuilder);
        client.IndexArrivalNames([_RawName, _StrictName]);

        var settled = new TaskCompletionSource<TransportMessage?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var acknowledged = false;
        var consumer = Substitute.For<IConsumer<byte[]>>();
        var receiveCount = 0;
        consumer
            .ReceiveAsync(Arg.Any<CancellationToken>())
            .Returns(async call =>
            {
                if (Interlocked.Increment(ref receiveCount) == 1)
                {
                    return delivery;
                }

                // Park the loop until the test cancels listening.
                await Task.Delay(Timeout.Infinite, call.Arg<CancellationToken>());
                return delivery;
            });
        consumer
            .AcknowledgeAsync(Arg.Any<MessageId>())
            .Returns(_ =>
            {
                acknowledged = true;
                settled.TrySetResult(null);

                // Pulsar.Client is F#: its acknowledgement task carries F#'s unit, which has no value.
                return Task.FromResult<Microsoft.FSharp.Core.Unit>(null!);
            });
        var negativelyAcknowledged = false;
        consumer
            .NegativeAcknowledge(Arg.Any<MessageId>())
            .Returns(_ =>
            {
                negativelyAcknowledged = true;
                settled.TrySetResult(null);
                return Task.FromResult<Microsoft.FSharp.Core.Unit>(null!);
            });
        typeof(PulsarConsumerClient)
            .GetField("_consumerClient", BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.DeclaredOnly)!
            .SetValue(client, consumer);

        var logs = new ConcurrentQueue<LogMessageEventArgs>();
        client.AttachCallbacks(
            (message, _) =>
            {
                settled.TrySetResult(message);
                return Task.CompletedTask;
            },
            logs.Enqueue
        );

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(AbortToken);
        var listening = client.ListeningAsync(TimeSpan.FromMilliseconds(10), cts.Token).AsTask();

        var delivered = await settled.Task.WaitAsync(TimeSpan.FromSeconds(5), AbortToken);
        await cts.CancelAsync();
        await listening.WaitAsync(TimeSpan.FromSeconds(5), AbortToken);

        return new ConsumeOutcome(delivered, acknowledged, negativelyAcknowledged, [.. logs]);
    }

    // Pulsar.Client builds Message<T> only on receipt and exposes no public constructor or factory for it.
    private static Message<byte[]> _Message(
        string topic,
        IReadOnlyDictionary<string, string> properties,
        int partition = -1,
        int batchIndex = -1,
        long publishTime = _PublishTimeMs
    )
    {
        var type =
            batchIndex < 0 ? MessageIdType.Single : MessageIdType.NewBatch((batchIndex, BatchMessageAcker.NullAcker));
        var messageId = new MessageId(11, 22, type, partition, topic, chunkMessageIds: null);
        var constructor = typeof(Message<byte[]>)
            .GetConstructors(BindingFlags.Instance | BindingFlags.NonPublic)
            .Single(candidate => candidate.GetParameters().Length == 17);

        return (Message<byte[]>)
            constructor.Invoke([
                messageId,
                "{}"u8.ToArray(),
                null,
                false,
                properties,
                null,
                null,
                0L,
                null,
                publishTime,
                null,
                0,
                null,
                null,
                null,
                null,
                null,
            ]);
    }

    private static MessageMetadata _Route(string name, string version, bool raw = false) =>
        new(
            new MessageRouteKey(typeof(object), name, MessageLane.Queue),
            typeof(object),
            version,
            CorrelationSelector: null,
            new Dictionary<Type, object>(),
            RawPayload: raw
        );

    private sealed record ConsumeOutcome(
        TransportMessage? Delivered,
        bool Acknowledged,
        bool NegativelyAcknowledged,
        IReadOnlyList<LogMessageEventArgs> Logs
    );
}
