// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Confluent.Kafka;
using Headless.Messaging;
using Headless.Messaging.Internal;
using Headless.Messaging.Kafka;
using Headless.Messaging.Transport;
using Headless.Testing.Tests;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using MessagingHeaders = Headless.Messaging.Headers;

namespace Tests;

public sealed class KafkaConsumerClientRawPayloadTests : TestBase
{
    private const string _RawName = "runs.result";
    private const string _StrictName = "orders.placed";
    private const long _StoredAtMs = 1_791_547_200_000;

    private readonly IOptions<KafkaMessagingOptions> _options = Options.Create(
        new KafkaMessagingOptions { Servers = "localhost:9092" }
    );

    private readonly TransportEnvelopeGate _gate = new([_Route(_RawName, "2", raw: true), _Route(_StrictName, "1")]);

    [Fact]
    public async Task should_name_the_arrival_message_only_when_its_topic_was_subscribed_by_name()
    {
        // given
        var consumer = Substitute.For<IConsumer<string, byte[]>>();
        await using var client = _CreateClient(consumer);

        // when
        await client.SubscribeAsync([_RawName, "^runs\\..*$"], AbortToken);

        // then
        client.ArrivalName(_RawName).Should().Be(_RawName);
        client.ArrivalName("runs.other").Should().BeNull();
        client.ArrivalName("^runs\\..*$").Should().BeNull();
    }

    [Fact]
    public async Task should_name_no_arrival_message_when_nothing_was_subscribed()
    {
        // given
        await using var client = _CreateClient(Substitute.For<IConsumer<string, byte[]>>());

        // when
        var arrivalName = client.ArrivalName(_RawName);

        // then
        arrivalName.Should().BeNull();
    }

    [Fact]
    public void should_identify_a_record_by_its_topic_partition_offset_and_timestamp()
    {
        // given
        var record = _Record(_RawName, headers: [], new Timestamp(_StoredAtMs, TimestampType.CreateTime));

        // when
        var facts = KafkaConsumerClient.NativeFacts(record);

        // then
        facts.ProducerMessageId.Should().BeNull();
        facts.PositionId.Should().Be($"kafka:{_RawName}:3:42:{_StoredAtMs}");
        facts.StoredAt.Should().Be(DateTimeOffset.FromUnixTimeMilliseconds(_StoredAtMs));
    }

    [Fact]
    public void should_give_no_stored_time_when_the_record_has_no_timestamp()
    {
        // given
        var record = _Record(_RawName, headers: [], new Timestamp(0, TimestampType.NotAvailable));

        // when
        var facts = KafkaConsumerClient.NativeFacts(record);

        // then
        facts.PositionId.Should().Be($"kafka:{_RawName}:3:42:0");
        facts.StoredAt.Should().BeNull();
    }

    [Fact]
    public async Task should_complete_a_headerless_record_when_its_topic_is_a_raw_contract()
    {
        // given
        var record = _Record(_RawName, headers: [], new Timestamp(_StoredAtMs, TimestampType.LogAppendTime));

        // when
        var (delivered, committed) = await _ConsumeOneAsync(record, subscribed: [_RawName]);

        // then
        committed.Should().BeFalse();
        var headers = delivered!.Value.Headers;
        var expectedId = $"kafka:{_RawName}:3:42:{_StoredAtMs}";
        headers[MessagingHeaders.MessageName].Should().Be(_RawName);
        headers[MessagingHeaders.MessageId].Should().Be(expectedId);
        headers[MessagingHeaders.ContractVersion].Should().Be("2");
        headers[MessagingHeaders.CorrelationId].Should().Be(expectedId);
        headers[MessagingHeaders.SentTime]
            .Should()
            .Be(DateTimeOffset.FromUnixTimeMilliseconds(_StoredAtMs).ToString("O", CultureInfo.InvariantCulture));
        headers[MessagingHeaders.TransportAddress].Should().Be(_RawName);
        delivered.Value.Body.ToArray().Should().Equal("{}"u8.ToArray());
    }

    [Fact]
    public async Task should_terminally_commit_a_headerless_record_when_its_topic_is_a_strict_contract()
    {
        // given
        var record = _Record(_StrictName, headers: [], new Timestamp(_StoredAtMs, TimestampType.CreateTime));

        // when
        var (delivered, committed) = await _ConsumeOneAsync(record, subscribed: [_StrictName]);

        // then
        delivered.Should().BeNull();
        committed.Should().BeTrue();
    }

    [Fact]
    public async Task should_terminally_commit_when_a_header_names_another_contract_on_a_raw_topic()
    {
        // given
        Confluent.Kafka.Headers headers =
        [
            new Header(MessagingHeaders.MessageName, Encoding.UTF8.GetBytes(_StrictName)),
        ];
        var record = _Record(_RawName, headers, new Timestamp(_StoredAtMs, TimestampType.CreateTime));

        // when
        var (delivered, committed) = await _ConsumeOneAsync(record, subscribed: [_RawName]);

        // then
        delivered.Should().BeNull();
        committed.Should().BeTrue();
    }

    [Fact]
    public async Task should_terminally_commit_a_headerless_record_when_only_a_wildcard_matched_its_topic()
    {
        // given
        var record = _Record(_RawName, headers: [], new Timestamp(_StoredAtMs, TimestampType.CreateTime));

        // when
        var (delivered, committed) = await _ConsumeOneAsync(record, subscribed: ["^runs\\..*$"]);

        // then
        delivered.Should().BeNull();
        committed.Should().BeTrue();
    }

    private KafkaConsumerClient _CreateClient(IConsumer<string, byte[]> consumer)
    {
        var serviceProvider = new ServiceCollection().AddSingleton(_gate).BuildServiceProvider();

        return new KafkaConsumerClient("test-group", 1, _options, serviceProvider, consumerFactory: _ => consumer);
    }

    private async Task<(TransportMessage? Delivered, bool Committed)> _ConsumeOneAsync(
        ConsumeResult<string, byte[]> record,
        string[] subscribed
    )
    {
        var consumer = Substitute.For<IConsumer<string, byte[]>>();
        var consumeCallCount = 0;
        consumer
            .Consume(Arg.Any<TimeSpan>())
            .Returns(_ =>
                Interlocked.Increment(ref consumeCallCount) == 1 ? record : throw new OperationCanceledException()
            );

        var committed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        consumer
            .When(c => c.StoreOffset(Arg.Is<TopicPartitionOffset>(offset => offset.Offset == record.Offset + 1)))
            .Do(_ => committed.TrySetResult());

        await using var client = _CreateClient(consumer);
        await client.SubscribeAsync(subscribed, AbortToken);

        var delivered = new TaskCompletionSource<TransportMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
        client.OnMessageCallback = (message, _) =>
        {
            delivered.TrySetResult(message);
            return Task.CompletedTask;
        };
        client.OnLogCallback = _ => { };

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));

        // Consume blocks, so run the loop off the test thread the way the consumer register does.
        var listeningTask = Task.Run(
            () => client.ListeningAsync(TimeSpan.FromMilliseconds(10), cts.Token).AsTask(),
            AbortToken
        );

        try
        {
            _ = await Task.WhenAny(delivered.Task, committed.Task).WaitAsync(TimeSpan.FromSeconds(2), AbortToken);

            return (
                delivered.Task.IsCompletedSuccessfully ? delivered.Task.Result : null,
                committed.Task.IsCompletedSuccessfully
            );
        }
        finally
        {
            await cts.CancelAsync();
            try
            {
                await listeningTask.WaitAsync(TimeSpan.FromSeconds(1), AbortToken);
            }
#pragma warning disable ERP022 // Best-effort cleanup observes the expected mocked cancellation.
            catch
            {
                // Best-effort cleanup only.
            }
#pragma warning restore ERP022
        }
    }

    private static ConsumeResult<string, byte[]> _Record(
        string topic,
        Confluent.Kafka.Headers headers,
        Timestamp timestamp
    )
    {
        return new ConsumeResult<string, byte[]>
        {
            TopicPartitionOffset = new TopicPartitionOffset(topic, new Partition(3), new Offset(42)),
            Message = new Message<string, byte[]>
            {
                Value = "{}"u8.ToArray(),
                Headers = headers,
                Timestamp = timestamp,
            },
        };
    }

    private static Headless.Messaging.Internal.MessageMetadata _Route(string name, string version, bool raw = false) =>
        new(
            new MessageRouteKey(typeof(object), name, MessageLane.Queue),
            typeof(object),
            version,
            CorrelationSelector: null,
            new Dictionary<Type, object>(),
            RawPayload: raw
        );
}
