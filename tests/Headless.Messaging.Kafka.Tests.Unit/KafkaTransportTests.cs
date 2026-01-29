// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Confluent.Kafka;
using Headless.Messaging.Internal;
using Headless.Messaging.Kafka;
using Headless.Messaging.Messages;
using Headless.Testing.Tests;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using MessagingHeaders = Headless.Messaging.Messages.Headers;

namespace Tests;

public sealed class KafkaTransportTests : TestBase
{
    private readonly ILogger<KafkaTransport> _logger = NullLogger<KafkaTransport>.Instance;
    private readonly IKafkaConnectionPool _pool;
    private readonly IProducer<string, byte[]> _producer;

    public KafkaTransportTests()
    {
        _pool = Substitute.For<IKafkaConnectionPool>();
        _producer = Substitute.For<IProducer<string, byte[]>>();

        _pool.ServersAddress.Returns("localhost:9092");
        _pool.RentProducer().Returns(_producer);
    }

    [Fact]
    public async Task should_have_correct_broker_address()
    {
        // given, when
        await using var transport = new KafkaTransport(_logger, _pool);

        // then
        transport.BrokerAddress.Name.Should().Be("Kafka");
        transport.BrokerAddress.Endpoint.Should().Be("localhost:9092");
    }

    [Fact]
    public async Task should_produce_message_successfully()
    {
        // given
        await using var transport = new KafkaTransport(_logger, _pool);
        var message = new TransportMessage(
            headers: new Dictionary<string, string?>(StringComparer.Ordinal)
            {
                { MessagingHeaders.MessageId, "msg-123" },
                { MessagingHeaders.MessageName, "TestTopic" },
            },
            body: "test-body"u8.ToArray()
        );

        var deliveryResult = new DeliveryResult<string, byte[]>
        {
            Status = PersistenceStatus.Persisted,
            Topic = "TestTopic",
        };
        _producer
            .ProduceAsync(Arg.Any<string>(), Arg.Any<Message<string, byte[]>>(), Arg.Any<CancellationToken>())
            .Returns(deliveryResult);

        // when
        var result = await transport.SendAsync(message);

        // then
        result.Succeeded.Should().BeTrue();
        _pool.Received(1).Return(_producer);
    }

    [Fact]
    public async Task should_return_producer_to_pool_after_publish()
    {
        // given
        await using var transport = new KafkaTransport(_logger, _pool);
        var message = new TransportMessage(
            headers: new Dictionary<string, string?>(StringComparer.Ordinal)
            {
                { MessagingHeaders.MessageId, "msg-123" },
                { MessagingHeaders.MessageName, "TestTopic" },
            },
            body: "test-body"u8.ToArray()
        );

        var deliveryResult = new DeliveryResult<string, byte[]>
        {
            Status = PersistenceStatus.Persisted,
            Topic = "TestTopic",
        };
        _producer
            .ProduceAsync(Arg.Any<string>(), Arg.Any<Message<string, byte[]>>(), Arg.Any<CancellationToken>())
            .Returns(deliveryResult);

        // when
        await transport.SendAsync(message);

        // then
        _pool.Received(1).Return(_producer);
    }

    [Fact]
    public async Task should_return_failed_result_on_exception()
    {
        // given
        await using var transport = new KafkaTransport(_logger, _pool);
        var message = new TransportMessage(
            headers: new Dictionary<string, string?>(StringComparer.Ordinal)
            {
                { MessagingHeaders.MessageId, "msg-123" },
                { MessagingHeaders.MessageName, "TestTopic" },
            },
            body: "test-body"u8.ToArray()
        );

        _producer
            .ProduceAsync(Arg.Any<string>(), Arg.Any<Message<string, byte[]>>(), Arg.Any<CancellationToken>())
            .Returns<DeliveryResult<string, byte[]>>(_ =>
                throw new ProduceException<string, byte[]>(
                    new Error(ErrorCode.Local_MsgTimedOut, "Timed out"),
                    new DeliveryResult<string, byte[]>()
                )
            );

        // when
        var result = await transport.SendAsync(message);

        // then
        result.Succeeded.Should().BeFalse();
        result.Exception.Should().BeOfType<PublisherSentFailedException>();
        _pool.Received(1).Return(_producer);
    }

    [Fact]
    public async Task should_use_message_id_as_key_by_default()
    {
        // given
        await using var transport = new KafkaTransport(_logger, _pool);
        var message = new TransportMessage(
            headers: new Dictionary<string, string?>(StringComparer.Ordinal)
            {
                { MessagingHeaders.MessageId, "msg-123" },
                { MessagingHeaders.MessageName, "TestTopic" },
            },
            body: "test-body"u8.ToArray()
        );

        var deliveryResult = new DeliveryResult<string, byte[]>
        {
            Status = PersistenceStatus.Persisted,
            Topic = "TestTopic",
        };
        _producer
            .ProduceAsync(Arg.Any<string>(), Arg.Any<Message<string, byte[]>>(), Arg.Any<CancellationToken>())
            .Returns(deliveryResult);

        // when
        await transport.SendAsync(message);

        // then
        await _producer
            .Received(1)
            .ProduceAsync(
                "TestTopic",
                Arg.Is<Message<string, byte[]>>(m => m.Key == "msg-123"),
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task should_use_custom_key_when_kafka_key_header_provided()
    {
        // given
        await using var transport = new KafkaTransport(_logger, _pool);
        var message = new TransportMessage(
            headers: new Dictionary<string, string?>(StringComparer.Ordinal)
            {
                { MessagingHeaders.MessageId, "msg-123" },
                { MessagingHeaders.MessageName, "TestTopic" },
                { KafkaHeaders.KafkaKey, "custom-partition-key" },
            },
            body: "test-body"u8.ToArray()
        );

        var deliveryResult = new DeliveryResult<string, byte[]>
        {
            Status = PersistenceStatus.Persisted,
            Topic = "TestTopic",
        };
        _producer
            .ProduceAsync(Arg.Any<string>(), Arg.Any<Message<string, byte[]>>(), Arg.Any<CancellationToken>())
            .Returns(deliveryResult);

        // when
        await transport.SendAsync(message);

        // then
        await _producer
            .Received(1)
            .ProduceAsync(
                "TestTopic",
                Arg.Is<Message<string, byte[]>>(m => m.Key == "custom-partition-key"),
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task should_use_message_id_when_kafka_key_is_empty()
    {
        // given
        await using var transport = new KafkaTransport(_logger, _pool);
        var message = new TransportMessage(
            headers: new Dictionary<string, string?>(StringComparer.Ordinal)
            {
                { MessagingHeaders.MessageId, "msg-123" },
                { MessagingHeaders.MessageName, "TestTopic" },
                { KafkaHeaders.KafkaKey, "" },
            },
            body: "test-body"u8.ToArray()
        );

        var deliveryResult = new DeliveryResult<string, byte[]>
        {
            Status = PersistenceStatus.Persisted,
            Topic = "TestTopic",
        };
        _producer
            .ProduceAsync(Arg.Any<string>(), Arg.Any<Message<string, byte[]>>(), Arg.Any<CancellationToken>())
            .Returns(deliveryResult);

        // when
        await transport.SendAsync(message);

        // then
        await _producer
            .Received(1)
            .ProduceAsync(
                "TestTopic",
                Arg.Is<Message<string, byte[]>>(m => m.Key == "msg-123"),
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task should_include_headers_in_published_message()
    {
        // given
        await using var transport = new KafkaTransport(_logger, _pool);
        var message = new TransportMessage(
            headers: new Dictionary<string, string?>(StringComparer.Ordinal)
            {
                { MessagingHeaders.MessageId, "msg-123" },
                { MessagingHeaders.MessageName, "TestTopic" },
                { "CustomHeader", "CustomValue" },
            },
            body: "test-body"u8.ToArray()
        );

        var deliveryResult = new DeliveryResult<string, byte[]>
        {
            Status = PersistenceStatus.Persisted,
            Topic = "TestTopic",
        };
        _producer
            .ProduceAsync(Arg.Any<string>(), Arg.Any<Message<string, byte[]>>(), Arg.Any<CancellationToken>())
            .Returns(deliveryResult);

        // when
        await transport.SendAsync(message);

        // then
        await _producer
            .Received(1)
            .ProduceAsync(
                Arg.Any<string>(),
                Arg.Is<Message<string, byte[]>>(m => m.Headers != null && m.Headers.Count == 3),
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task should_handle_null_header_values()
    {
        // given
        await using var transport = new KafkaTransport(_logger, _pool);
        var message = new TransportMessage(
            headers: new Dictionary<string, string?>(StringComparer.Ordinal)
            {
                { MessagingHeaders.MessageId, "msg-123" },
                { MessagingHeaders.MessageName, "TestTopic" },
                { "NullHeader", null },
            },
            body: "test-body"u8.ToArray()
        );

        var deliveryResult = new DeliveryResult<string, byte[]>
        {
            Status = PersistenceStatus.Persisted,
            Topic = "TestTopic",
        };
        _producer
            .ProduceAsync(Arg.Any<string>(), Arg.Any<Message<string, byte[]>>(), Arg.Any<CancellationToken>())
            .Returns(deliveryResult);

        // when
        var result = await transport.SendAsync(message);

        // then
        result.Succeeded.Should().BeTrue();
    }

    [Fact]
    public async Task should_succeed_when_status_is_PossiblyPersisted()
    {
        // given
        await using var transport = new KafkaTransport(_logger, _pool);
        var message = new TransportMessage(
            headers: new Dictionary<string, string?>(StringComparer.Ordinal)
            {
                { MessagingHeaders.MessageId, "msg-123" },
                { MessagingHeaders.MessageName, "TestTopic" },
            },
            body: "test-body"u8.ToArray()
        );

        var deliveryResult = new DeliveryResult<string, byte[]>
        {
            Status = PersistenceStatus.PossiblyPersisted,
            Topic = "TestTopic",
        };
        _producer
            .ProduceAsync(Arg.Any<string>(), Arg.Any<Message<string, byte[]>>(), Arg.Any<CancellationToken>())
            .Returns(deliveryResult);

        // when
        var result = await transport.SendAsync(message);

        // then
        result.Succeeded.Should().BeTrue();
    }

    [Fact]
    public async Task should_fail_when_status_is_NotPersisted()
    {
        // given
        await using var transport = new KafkaTransport(_logger, _pool);
        var message = new TransportMessage(
            headers: new Dictionary<string, string?>(StringComparer.Ordinal)
            {
                { MessagingHeaders.MessageId, "msg-123" },
                { MessagingHeaders.MessageName, "TestTopic" },
            },
            body: "test-body"u8.ToArray()
        );

        var deliveryResult = new DeliveryResult<string, byte[]>
        {
            Status = PersistenceStatus.NotPersisted,
            Topic = "TestTopic",
        };
        _producer
            .ProduceAsync(Arg.Any<string>(), Arg.Any<Message<string, byte[]>>(), Arg.Any<CancellationToken>())
            .Returns(deliveryResult);

        // when
        var result = await transport.SendAsync(message);

        // then
        result.Succeeded.Should().BeFalse();
        result.Exception.Should().BeOfType<PublisherSentFailedException>();
        result.Exception!.Message.Should().Contain("persisted failed");
    }

    [Fact]
    public async Task should_return_producer_to_pool_even_on_failure()
    {
        // given
        await using var transport = new KafkaTransport(_logger, _pool);
        var message = new TransportMessage(
            headers: new Dictionary<string, string?>(StringComparer.Ordinal)
            {
                { MessagingHeaders.MessageId, "msg-123" },
                { MessagingHeaders.MessageName, "TestTopic" },
            },
            body: "test-body"u8.ToArray()
        );

        var deliveryResult = new DeliveryResult<string, byte[]>
        {
            Status = PersistenceStatus.NotPersisted,
            Topic = "TestTopic",
        };
        _producer
            .ProduceAsync(Arg.Any<string>(), Arg.Any<Message<string, byte[]>>(), Arg.Any<CancellationToken>())
            .Returns(deliveryResult);

        // when
        await transport.SendAsync(message);

        // then
        _pool.Received(1).Return(_producer);
    }

    [Fact]
    public async Task DisposeAsync_should_complete_without_error()
    {
        // given
        var transport = new KafkaTransport(_logger, _pool);

        // when
        await transport.DisposeAsync();

        // then - no exception thrown
    }
}
