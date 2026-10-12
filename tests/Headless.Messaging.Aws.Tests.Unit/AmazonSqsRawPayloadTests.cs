// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Reflection;
using Amazon.SQS;
using Amazon.SQS.Model;
using Headless.Messaging;
using Headless.Messaging.Aws;
using Headless.Messaging.Internal;
using Headless.Messaging.Transport;
using Headless.Testing.Tests;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SqsAttribute = Amazon.SQS.Model.MessageAttributeValue;
using SqsMessage = Amazon.SQS.Model.Message;

namespace Tests;

/// <summary>
/// A message a foreign producer sent with the AWS SDK, without the Headless header bag, reaches a contract declared with
/// <c>RawPayload()</c> by the queue it arrived on, with an identity every redelivery keeps.
/// </summary>
public sealed class AmazonSqsRawPayloadTests : TestBase
{
    private const string _RawName = "runs.result";
    private const string _StrictName = "orders.placed";
    private const string _Identity = "runs";
    private const string _BusQueueUrl = "https://sqs.local/000000000000/bus-runs";
    private const long _SentTimestamp = 1_791_547_200_000;

    private readonly IAmazonSQS _sqs = Substitute.For<IAmazonSQS>();
    private readonly ILogger<AmazonSqsConsumerClient> _logger = Substitute.For<ILogger<AmazonSqsConsumerClient>>();
    private readonly List<TransportMessage> _received = [];

    public AmazonSqsRawPayloadTests()
    {
        _logger.IsEnabled(Arg.Any<LogLevel>()).Returns(true);
    }

    [Fact]
    public async Task should_complete_a_headerless_queue_message_when_its_queue_belongs_to_a_raw_contract()
    {
        // given
        var queueUrl = _QueueUrl(_RawName);
        await using var client = await _SubscribeQueueAsync(_RawName, queueUrl);

        // when
        await _ConsumeAsync(client, queueUrl, _Message("sqs-message-1", "receipt-1"));

        // then
        _received.Should().ContainSingle();
        var headers = _received[0].Headers;
        headers[Headers.MessageName].Should().Be(_RawName);
        headers[Headers.MessageId].Should().Be("sqs:sqs-message-1");
        headers[Headers.ContractVersion].Should().Be("2");
        headers[Headers.CorrelationId].Should().Be("sqs:sqs-message-1");
        headers[Headers.SentTime]
            .Should()
            .Be(DateTimeOffset.FromUnixTimeMilliseconds(_SentTimestamp).ToString("O", CultureInfo.InvariantCulture));
        headers[Headers.TransportAddress].Should().Be(queueUrl);
        _received[0].Body.ToArray().Should().Equal("{\"ok\":true}"u8.ToArray());
        await _AssertNotDeletedAsync(queueUrl, "receipt-1");
    }

    [Fact]
    public async Task should_give_every_receive_of_a_headerless_message_the_same_message_id()
    {
        // given: SQS keeps the MessageId across receives and hands out a new receipt handle each time
        var queueUrl = _QueueUrl(_RawName);
        await using var client = await _SubscribeQueueAsync(_RawName, queueUrl);

        // when
        await _ConsumeAsync(client, queueUrl, _Message("sqs-message-1", "receipt-1"));
        await _ConsumeAsync(client, queueUrl, _Message("sqs-message-1", "receipt-2"));

        // then
        _received.Should().HaveCount(2);
        _received[1].Headers[Headers.MessageId].Should().Be(_received[0].Headers[Headers.MessageId]);
    }

    [Fact]
    public async Task should_ignore_foreign_attributes_when_a_raw_message_has_no_header_bag()
    {
        // given
        var queueUrl = _QueueUrl(_RawName);
        await using var client = await _SubscribeQueueAsync(_RawName, queueUrl);
        var message = _Message(
            "sqs-message-1",
            "receipt-1",
            new Dictionary<string, SqsAttribute>(StringComparer.Ordinal)
            {
                ["traceparent"] = new() { DataType = "String", StringValue = "00-abc-def-01" },
            }
        );

        // when
        await _ConsumeAsync(client, queueUrl, message);

        // then
        _received.Should().ContainSingle();
        _received[0].Headers.Should().NotContainKey("traceparent");
        _received[0].Headers[Headers.MessageName].Should().Be(_RawName);
    }

    [Fact]
    public async Task should_keep_the_header_bag_rules_when_a_raw_message_carries_the_bag()
    {
        // given: a bag beside another attribute is a broken Headless envelope, not a foreign message
        var queueUrl = _QueueUrl(_RawName);
        await using var client = await _SubscribeQueueAsync(_RawName, queueUrl);
        var attributes = _HeaderBag((Headers.MessageId, "id-1"), (Headers.MessageName, _RawName));
        attributes["extra"] = new() { DataType = "String", StringValue = "value" };

        // when
        await _ConsumeAsync(client, queueUrl, _Message("sqs-message-1", "receipt-1", attributes));

        // then
        _received.Should().BeEmpty();
        await _AssertDeletedAsync(queueUrl, "receipt-1");
    }

    [Fact]
    public async Task should_delete_a_headerless_message_when_its_queue_belongs_to_a_strict_contract()
    {
        // given
        var queueUrl = _QueueUrl(_StrictName);
        await using var client = await _SubscribeQueueAsync(_StrictName, queueUrl);

        // when
        await _ConsumeAsync(client, queueUrl, _Message("sqs-message-1", "receipt-1"));

        // then
        _received.Should().BeEmpty();
        _AssertLogged(4200);
        await _AssertDeletedAsync(queueUrl, "receipt-1");
    }

    [Fact]
    public async Task should_delete_a_raw_route_message_when_its_header_names_another_contract()
    {
        // given
        var queueUrl = _QueueUrl(_RawName);
        await using var client = await _SubscribeQueueAsync(_RawName, queueUrl);
        var attributes = _HeaderBag((Headers.MessageId, "id-1"), (Headers.MessageName, _StrictName));

        // when
        await _ConsumeAsync(client, queueUrl, _Message("sqs-message-1", "receipt-1", attributes));

        // then
        _received.Should().BeEmpty();
        _AssertLogged(4202);
        await _AssertDeletedAsync(queueUrl, "receipt-1");
    }

    [Fact]
    public async Task should_not_name_a_contract_when_the_polled_queue_is_not_the_contracts_queue()
    {
        // given: a caller subscribed the raw contract's client to some other queue
        const string otherQueueUrl = "https://sqs.local/000000000000/some-other-queue";
        await using var client = await _SubscribeQueueAsync(_RawName, otherQueueUrl);

        // when
        await _ConsumeAsync(client, otherQueueUrl, _Message("sqs-message-1", "receipt-1"));

        // then
        _received.Should().BeEmpty();
        await _AssertDeletedAsync(otherQueueUrl, "receipt-1");
    }

    [Fact]
    public async Task should_complete_a_headerless_bus_message_when_the_identity_queue_carries_only_the_raw_contract()
    {
        // given
        await using var client = _CreateClient(MessageLane.Bus, _Identity);
        await client.SubscribeAsync([_RawName], AbortToken);

        // when
        await _ConsumeAsync(client, _BusQueueUrl, _Message("sqs-message-1", "receipt-1"));

        // then
        _received.Should().ContainSingle();
        _received[0].Headers[Headers.MessageName].Should().Be(_RawName);
        _received[0].Headers[Headers.MessageId].Should().Be("sqs:sqs-message-1");
    }

    [Fact]
    public async Task should_refuse_to_start_when_a_raw_bus_contract_shares_its_identity_queue()
    {
        // given
        await using var client = _CreateClient(MessageLane.Bus, _Identity);

        // when
        var fetch = async () => await client.FetchMessageNamesAsync([_RawName, _StrictName], AbortToken);
        var subscribe = async () => await client.SubscribeAsync([_StrictName, _RawName], AbortToken);

        // then
        await fetch
            .Should()
            .ThrowAsync<InvalidOperationException>()
            .WithMessage($"*'{_Identity}'*'{_RawName}'*'{_StrictName}'*");
        await subscribe
            .Should()
            .ThrowAsync<InvalidOperationException>()
            .WithMessage($"*'{_Identity}'*'{_RawName}'*'{_StrictName}'*");
    }

    [Fact]
    public async Task should_keep_header_routing_when_a_bus_identity_queue_carries_several_strict_contracts()
    {
        // given
        await using var client = _CreateClient(MessageLane.Bus, _Identity);
        await client.SubscribeAsync([_StrictName, "orders.shipped"], AbortToken);
        var attributes = _HeaderBag((Headers.MessageId, "id-1"), (Headers.MessageName, _StrictName));

        // when
        await _ConsumeAsync(client, _BusQueueUrl, _Message("sqs-message-1", "receipt-1"));
        await _ConsumeAsync(client, _BusQueueUrl, _Message("sqs-message-2", "receipt-2", attributes));

        // then
        _received.Should().ContainSingle().Which.Headers[Headers.MessageId].Should().Be("id-1");
        await _AssertDeletedAsync(_BusQueueUrl, "receipt-1");
    }

    [Fact]
    public async Task should_let_the_custom_headers_builder_supply_the_identity_before_the_gate()
    {
        // given: the builder overwrites the wire value and runs before the raw envelope is completed
        var queueUrl = _QueueUrl(_RawName);
        await using var client = await _SubscribeQueueAsync(
            _RawName,
            queueUrl,
            options =>
                options.CustomHeadersBuilder = (message, _) =>
                    [
                        new(Headers.MessageId, "producer-" + message.MessageId),
                        new(Headers.TransportAddress, "chosen-by-builder"),
                    ]
        );

        // when
        await _ConsumeAsync(client, queueUrl, _Message("sqs-message-1", "receipt-1"));

        // then
        _received.Should().ContainSingle();
        _received[0].Headers[Headers.MessageId].Should().Be("producer-sqs-message-1");
        _received[0].Headers[Headers.TransportAddress].Should().Be(queueUrl);
    }

    [Fact]
    public async Task should_delete_the_message_when_the_custom_headers_builder_throws()
    {
        // given
        var queueUrl = _QueueUrl(_RawName);
        await using var client = await _SubscribeQueueAsync(
            _RawName,
            queueUrl,
            options => options.CustomHeadersBuilder = (_, _) => throw new FormatException("bad attribute")
        );

        // when
        await _ConsumeAsync(client, queueUrl, _Message("sqs-message-1", "receipt-1"));

        // then
        _received.Should().BeEmpty();
        _AssertLogged(4202);
        await _AssertDeletedAsync(queueUrl, "receipt-1");
    }

    [Fact]
    public async Task should_ask_sqs_for_the_sent_timestamp_on_every_receive()
    {
        // given
        await using var client = _CreateClient(MessageLane.Queue, _RawName);
        ReceiveMessageRequest? request = null;
        _sqs.ReceiveMessageAsync(Arg.Do<ReceiveMessageRequest>(r => request = r), Arg.Any<CancellationToken>())
            .Returns(new ReceiveMessageResponse { Messages = [] });
        var receive = typeof(AmazonSqsConsumerClient).GetMethod(
            "_ReceiveMessagesAsync",
            BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.DeclaredOnly,
            binder: null,
            [typeof(string), typeof(CancellationToken)],
            modifiers: null
        )!;

        // when
        await (Task)receive.Invoke(client, ["https://sqs.local/queue", AbortToken])!;

        // then
        request.Should().NotBeNull();
        request!.MessageSystemAttributeNames.Should().Contain("SentTimestamp");
        request.MessageAttributeNames.Should().Contain("All");
    }

    [Fact]
    public void should_derive_the_position_id_and_send_time_from_the_sqs_message()
    {
        // when
        var facts = AmazonSqsConsumerClient.ReadNativeFacts(_Message("sqs-message-1", "receipt-1"));

        // then: SQS has no producer-set message id field
        facts.ProducerMessageId.Should().BeNull();
        facts.PositionId.Should().Be("sqs:sqs-message-1");
        facts.StoredAt.Should().Be(DateTimeOffset.FromUnixTimeMilliseconds(_SentTimestamp));
    }

    [Fact]
    public void should_derive_no_identity_when_the_sqs_message_has_no_message_id()
    {
        // when
        var facts = AmazonSqsConsumerClient.ReadNativeFacts(new SqsMessage { ReceiptHandle = "receipt-1" });

        // then
        facts.ProducerMessageId.Should().BeNull();
        facts.PositionId.Should().BeNull();
        facts.StoredAt.Should().BeNull();
    }

    private async Task<AmazonSqsConsumerClient> _SubscribeQueueAsync(
        string messageName,
        string queueUrl,
        Action<AmazonSqsMessagingOptions>? configure = null
    )
    {
        var client = _CreateClient(MessageLane.Queue, messageName, configure);
        await client.SubscribeAsync([queueUrl], AbortToken);
        return client;
    }

    private AmazonSqsConsumerClient _CreateClient(
        MessageLane lane,
        string subscriptionName,
        Action<AmazonSqsMessagingOptions>? configure = null
    )
    {
        // AutoProvision off keeps SNS out of a Bus subscribe: these tests drive the receive path only.
        var options = new AmazonSqsMessagingOptions
        {
            Region = Amazon.RegionEndpoint.USEast1,
            SqsServiceUrl = "http://localhost:4566",
            SnsServiceUrl = "http://localhost:4566",
            AutoProvision = false,
        };
        configure?.Invoke(options);

        var services = new ServiceCollection();
        services.AddSingleton(
            new TransportEnvelopeGate([
                _Route(_RawName, "2", lane, raw: true),
                _Route(_StrictName, "1", lane),
                _Route("orders.shipped", "1", lane),
            ])
        );

        var client = new AmazonSqsConsumerClient(
            subscriptionName,
            1,
            Options.Create(options),
            _logger,
            lane,
            serviceProvider: services.BuildServiceProvider()
        );
        _SetField(client, "_sqsClient", _sqs);
        _SetField(client, "_queueUrl", _BusQueueUrl);
        client.AttachCallbacks(
            (message, _) =>
            {
                _received.Add(message);
                return Task.CompletedTask;
            },
            _ => { }
        );

        return client;
    }

    private static Task _ConsumeAsync(AmazonSqsConsumerClient client, string queueUrl, SqsMessage message)
    {
        var consume = typeof(AmazonSqsConsumerClient).GetMethod(
            "_ConsumeAsync",
            BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.DeclaredOnly,
            binder: null,
            [typeof(InflightSqsMessage), typeof(SqsMessage)],
            modifiers: null
        )!;

        return (Task)consume.Invoke(client, [new InflightSqsMessage(queueUrl, message.ReceiptHandle), message])!;
    }

    private static void _SetField(AmazonSqsConsumerClient client, string name, object value)
    {
        typeof(AmazonSqsConsumerClient)
            .GetField(name, BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.DeclaredOnly)!
            .SetValue(client, value);
    }

    private static string _QueueUrl(string messageName) =>
        "https://sqs.local/000000000000/" + AwsPhysicalAddress.QueueDestination(messageName).NormalizeForSqsQueueName();

    private static SqsMessage _Message(
        string messageId,
        string receiptHandle,
        Dictionary<string, SqsAttribute>? attributes = null
    ) =>
        new()
        {
            MessageId = messageId,
            ReceiptHandle = receiptHandle,
            Body = "{\"ok\":true}",
            MessageAttributes = attributes,
            Attributes = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["SentTimestamp"] = _SentTimestamp.ToString(CultureInfo.InvariantCulture),
            },
        };

    private static Dictionary<string, SqsAttribute> _HeaderBag(params (string Name, string? Value)[] headers) =>
        new(StringComparer.Ordinal)
        {
            [SqsHeaderCodec.AttributeName] = new()
            {
                DataType = "String",
                StringValue = JsonSerializer.Serialize(
                    headers.ToDictionary(h => h.Name, h => h.Value, StringComparer.Ordinal)
                ),
            },
        };

    private async Task _AssertDeletedAsync(string queueUrl, string receiptHandle)
    {
        await _sqs.Received(1).DeleteMessageAsync(queueUrl, receiptHandle, Arg.Any<CancellationToken>());
    }

    private async Task _AssertNotDeletedAsync(string queueUrl, string receiptHandle)
    {
        await _sqs.DidNotReceive().DeleteMessageAsync(queueUrl, receiptHandle, Arg.Any<CancellationToken>());
    }

    private void _AssertLogged(int eventId)
    {
        var eventIds = _logger
            .ReceivedCalls()
            .Where(call => string.Equals(call.GetMethodInfo().Name, nameof(ILogger.Log), StringComparison.Ordinal))
            .Select(call => ((EventId)call.GetArguments()[1]!).Id);

        eventIds.Should().Contain(eventId);
    }

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
