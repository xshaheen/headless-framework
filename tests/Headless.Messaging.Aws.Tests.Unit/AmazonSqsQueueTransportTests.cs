// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Collections.Concurrent;
using System.Reflection;
using Amazon.SQS;
using Amazon.SQS.Model;
using Headless.Messaging;
using Headless.Messaging.Aws;
using Headless.Testing.Tests;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NSubstitute.ExceptionExtensions;

namespace Tests;

public sealed class AmazonSqsQueueTransportTests : TestBase
{
    private static IOptions<AmazonSqsMessagingOptions> _CreateOptions()
    {
        return Options.Create(
            new AmazonSqsMessagingOptions
            {
                Region = Amazon.RegionEndpoint.USEast1,
                SqsServiceUrl = "http://localhost:4566",
                SnsServiceUrl = "http://localhost:4566",
            }
        );
    }

    [Theory]
    [InlineData(null)]
    [InlineData("order-42")]
    public async Task should_map_affinity_to_fifo_message_group(string? raw)
    {
        await using var transport = new AmazonSqsQueueTransport(
            Substitute.For<ILogger<AmazonSqsQueueTransport>>(),
            _CreateOptions()
        );
        var client = Substitute.For<IAmazonSQS>();
        _SetSqsClient(transport, client);
        _SetQueueUrl(transport, "queue-orders.fifo", "https://sqs.local/orders.fifo");
        client
            .SendMessageAsync(Arg.Any<SendMessageRequest>(), Arg.Any<CancellationToken>())
            .Returns(new SendMessageResponse());
        var headers = new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            [Headers.MessageId] = "message-1",
            [Headers.MessageName] = "orders.fifo",
            [Headers.RoutingAffinityKey] = "order-42",
        };
        if (raw is not null)
        {
            headers[AwsMessagingHeaders.MessageGroupId] = raw;
        }

        var result = await transport.SendAsync(new TransportMessage(headers, "payload"u8.ToArray()), AbortToken);

        result.Succeeded.Should().BeTrue();
        await client
            .Received(1)
            .SendMessageAsync(Arg.Is<SendMessageRequest>(request => request.MessageGroupId == "order-42"), AbortToken);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("order-42")]
    public async Task should_pack_all_headers_losslessly_beyond_sqs_attribute_limit(string? key)
    {
        await using var transport = new AmazonSqsQueueTransport(
            Substitute.For<ILogger<AmazonSqsQueueTransport>>(),
            _CreateOptions()
        );
        var client = Substitute.For<IAmazonSQS>();
        _SetSqsClient(transport, client);
        _SetQueueUrl(transport, key is null ? "queue-orders" : "queue-orders.fifo", "https://sqs.local/orders");
        SendMessageRequest? sent = null;
        client
            .SendMessageAsync(Arg.Any<SendMessageRequest>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                sent = call.Arg<SendMessageRequest>();
                return new SendMessageResponse();
            });
        var headers = new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            [Headers.MessageId] = "message-1",
            [Headers.MessageName] = key is null ? "orders" : "orders.fifo",
            [Headers.Intent] = nameof(MessageLane.Queue),
            [Headers.ContractVersion] = "1",
            [Headers.Type] = "Order",
            [Headers.CorrelationId] = "correlation-1",
            [Headers.CorrelationSequence] = "0",
            [Headers.SentTime] = "2026-09-05T00:00:00Z",
            [Headers.RequestedDeliveryMode] = nameof(DeliveryMode.TransportDirect),
            [Headers.ResolvedDeliveryMode] = nameof(DeliveryMode.TransportDirect),
            [Headers.TraceParent] = "00-4bf92f3577b34da6a3ce929d0e0e4736-00f067aa0ba902b7-01",
            ["business-metadata"] = "preserved",
            ["optional-metadata"] = null,
        };

        if (key is not null)
        {
            headers[Headers.RoutingAffinityKey] = key;
        }

        var result = await transport.SendAsync(new TransportMessage(headers, "payload"u8.ToArray()), AbortToken);

        result.Succeeded.Should().BeTrue();
        sent.Should().NotBeNull();
        sent!.MessageBody.Should().Be("payload");
        sent.MessageGroupId.Should().Be(key);
        sent.MessageAttributes.Should().ContainSingle();
        var restored = JsonSerializer.Deserialize<Dictionary<string, string?>>(
            sent.MessageAttributes["headless-aws-headers-v1"].StringValue
        );
        restored.Should().BeEquivalentTo(headers);
    }

    [Theory]
    [InlineData("orders", "order-42", null)]
    [InlineData("orders.fifo", "order 42", null)]
    [InlineData("orders.fifo", "order-42", "other")]
    public async Task should_reject_invalid_affinity_before_aws_effects(string destination, string key, string? raw)
    {
        await using var transport = new AmazonSqsQueueTransport(
            Substitute.For<ILogger<AmazonSqsQueueTransport>>(),
            _CreateOptions()
        );
        var client = Substitute.For<IAmazonSQS>();
        _SetSqsClient(transport, client);
        var headers = new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            [Headers.MessageId] = "message-1",
            [Headers.MessageName] = destination,
            [Headers.RoutingAffinityKey] = key,
        };
        if (raw is not null)
        {
            headers[AwsMessagingHeaders.MessageGroupId] = raw;
        }

        var result = await transport.SendAsync(new TransportMessage(headers, "payload"u8.ToArray()), AbortToken);

        result.Succeeded.Should().BeFalse();
        client.ReceivedCalls().Should().BeEmpty();
    }

    [Theory]
    [InlineData(null, "headless-aws-headers-v1")]
    [InlineData("order-42", "headless-aws-headers-v1")]
    public async Task should_reject_reserved_header_bag_collision_before_clients(string? key, string reserved)
    {
        await using var transport = new AmazonSqsQueueTransport(
            Substitute.For<ILogger<AmazonSqsQueueTransport>>(),
            _CreateOptions()
        );
        var client = Substitute.For<IAmazonSQS>();
        _SetSqsClient(transport, client);
        var headers = new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            [Headers.MessageId] = "message-1",
            [Headers.MessageName] = "orders.fifo",
            [reserved] = "{}",
        };
        if (key is not null)
        {
            headers[Headers.RoutingAffinityKey] = key;
        }

        var result = await transport.SendAsync(new TransportMessage(headers, "payload"u8.ToArray()), AbortToken);

        result.Succeeded.Should().BeFalse();
        client.ReceivedCalls().Should().BeEmpty();
    }

    [Fact]
    public async Task should_return_correct_broker_address()
    {
        // given
        var logger = Substitute.For<ILogger<AmazonSqsQueueTransport>>();
        await using var transport = new AmazonSqsQueueTransport(logger, _CreateOptions());

        // when
        var brokerAddress = transport.BrokerAddress;

        // then
        brokerAddress.Name.Should().Be("aws_sqs");
        brokerAddress.Endpoint.Should().Be("localhost:4566");
    }

    [Fact]
    public async Task should_send_message_to_cached_queue_url()
    {
        // given
        var logger = Substitute.For<ILogger<AmazonSqsQueueTransport>>();
        await using var transport = new AmazonSqsQueueTransport(logger, _CreateOptions());

        var sqsClient = Substitute.For<IAmazonSQS>();
        sqsClient
            .SendMessageAsync(Arg.Any<SendMessageRequest>(), Arg.Any<CancellationToken>())
            .Returns(new SendMessageResponse { MessageId = "msg-123" });

        _SetSqsClient(transport, sqsClient);
        _SetQueueUrl(transport, "queue-OrderCreated", "https://sqs.local/queue-OrderCreated");

        var message = new TransportMessage(
            headers: new Dictionary<string, string?>(StringComparer.Ordinal)
            {
                [Headers.MessageName] = "OrderCreated",
                [Headers.MessageId] = "message-1",
                ["custom-header"] = "custom-value",
            },
            body: """{"id":42}"""u8.ToArray()
        );

        // when
        var result = await transport.SendAsync(message, AbortToken);

        // then
        result.Succeeded.Should().BeTrue();
        await sqsClient.DidNotReceive().CreateQueueAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
        await sqsClient
            .Received(1)
            .SendMessageAsync(
                Arg.Is<SendMessageRequest>(r =>
                    r.QueueUrl == "https://sqs.local/queue-OrderCreated"
                    && r.MessageBody == """{"id":42}"""
                    && SqsHeaderCodec.Decode(r.MessageAttributes)[Headers.MessageId] == "message-1"
                    && SqsHeaderCodec.Decode(r.MessageAttributes)["custom-header"] == "custom-value"
                ),
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task should_create_queue_when_url_not_cached()
    {
        // given
        var logger = Substitute.For<ILogger<AmazonSqsQueueTransport>>();
        await using var transport = new AmazonSqsQueueTransport(logger, _CreateOptions());
        var expectedQueueName = AwsPhysicalAddress.QueueDestination("order.created");

        var sqsClient = Substitute.For<IAmazonSQS>();
        sqsClient
            .CreateQueueAsync(expectedQueueName, Arg.Any<CancellationToken>())
            .Returns(new CreateQueueResponse { QueueUrl = $"https://sqs.local/{expectedQueueName}" });
        sqsClient
            .SendMessageAsync(Arg.Any<SendMessageRequest>(), Arg.Any<CancellationToken>())
            .Returns(new SendMessageResponse { MessageId = "msg-123" });

        _SetSqsClient(transport, sqsClient);

        var message = new TransportMessage(
            headers: new Dictionary<string, string?>(StringComparer.Ordinal)
            {
                [Headers.MessageName] = "order.created",
            },
            body: "test"u8.ToArray()
        );

        // when
        var result = await transport.SendAsync(message, AbortToken);

        // then
        result.Succeeded.Should().BeTrue();
        await sqsClient.Received(1).CreateQueueAsync(expectedQueueName, Arg.Any<CancellationToken>());
        await sqsClient
            .Received(1)
            .SendMessageAsync(
                Arg.Is<SendMessageRequest>(r => r.QueueUrl == $"https://sqs.local/{expectedQueueName}"),
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task should_create_fifo_queue_with_fifo_attributes_when_url_not_cached()
    {
        // given
        var logger = Substitute.For<ILogger<AmazonSqsQueueTransport>>();
        await using var transport = new AmazonSqsQueueTransport(logger, _CreateOptions());
        var expectedQueueName = AwsPhysicalAddress.QueueDestination("order.created.fifo");

        var sqsClient = Substitute.For<IAmazonSQS>();
        sqsClient
            .CreateQueueAsync(Arg.Any<CreateQueueRequest>(), Arg.Any<CancellationToken>())
            .Returns(new CreateQueueResponse { QueueUrl = $"https://sqs.local/{expectedQueueName}" });
        sqsClient
            .SendMessageAsync(Arg.Any<SendMessageRequest>(), Arg.Any<CancellationToken>())
            .Returns(new SendMessageResponse { MessageId = "msg-123" });

        _SetSqsClient(transport, sqsClient);

        var message = new TransportMessage(
            headers: new Dictionary<string, string?>(StringComparer.Ordinal)
            {
                [Headers.MessageName] = "order.created.fifo",
                [Headers.MessageId] = "message-1",
                [Headers.Group] = "tenant-a",
            },
            body: "test"u8.ToArray()
        );

        // when
        var result = await transport.SendAsync(message, AbortToken);

        // then
        result.Succeeded.Should().BeTrue();
        await sqsClient
            .Received(1)
            .CreateQueueAsync(
                Arg.Is<CreateQueueRequest>(r =>
                    r.QueueName == expectedQueueName
                    && r.Attributes["FifoQueue"] == "true"
                    && r.Attributes["ContentBasedDeduplication"] == "true"
                ),
                Arg.Any<CancellationToken>()
            );
        await sqsClient
            .Received(1)
            .SendMessageAsync(
                Arg.Is<SendMessageRequest>(r =>
                    r.MessageGroupId == "tenant-a" && r.MessageDeduplicationId == "message-1"
                ),
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task should_prefer_explicit_message_group_id_header_for_fifo_queue()
    {
        // given
        var logger = Substitute.For<ILogger<AmazonSqsQueueTransport>>();
        await using var transport = new AmazonSqsQueueTransport(logger, _CreateOptions());

        var sqsClient = Substitute.For<IAmazonSQS>();
        sqsClient
            .CreateQueueAsync(Arg.Any<CreateQueueRequest>(), Arg.Any<CancellationToken>())
            .Returns(new CreateQueueResponse { QueueUrl = "https://sqs.local/queue-order-created.fifo" });
        sqsClient
            .SendMessageAsync(Arg.Any<SendMessageRequest>(), Arg.Any<CancellationToken>())
            .Returns(new SendMessageResponse { MessageId = "msg-123" });

        _SetSqsClient(transport, sqsClient);

        var message = new TransportMessage(
            headers: new Dictionary<string, string?>(StringComparer.Ordinal)
            {
                [Headers.MessageName] = "order.created.fifo",
                [Headers.MessageId] = "message-1",
                [Headers.Group] = "tenant-a",
                [AwsMessagingHeaders.MessageGroupId] = "tenant-b",
            },
            body: "test"u8.ToArray()
        );

        // when
        var result = await transport.SendAsync(message, AbortToken);

        // then
        result.Succeeded.Should().BeTrue();
        await sqsClient
            .Received(1)
            .SendMessageAsync(
                Arg.Is<SendMessageRequest>(r => r.MessageGroupId == "tenant-b"),
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task should_return_failed_when_send_fails()
    {
        // given
        var logger = Substitute.For<ILogger<AmazonSqsQueueTransport>>();
        await using var transport = new AmazonSqsQueueTransport(logger, _CreateOptions());

        var sqsClient = Substitute.For<IAmazonSQS>();
        sqsClient
            .SendMessageAsync(Arg.Any<SendMessageRequest>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new AmazonSQSException("Network error"));

        _SetSqsClient(transport, sqsClient);
        _SetQueueUrl(transport, "queue-OrderCreated", "https://sqs.local/queue-OrderCreated");

        var message = new TransportMessage(
            headers: new Dictionary<string, string?>(StringComparer.Ordinal) { [Headers.MessageName] = "OrderCreated" },
            body: "test"u8.ToArray()
        );

        // when
        var result = await transport.SendAsync(message, AbortToken);

        // then
        result.Succeeded.Should().BeFalse();
        result.Exception.Should().NotBeNull();
        result.Exception!.Message.Should().Contain("Network error");
    }

    [Fact]
    public async Task should_propagate_cancellation()
    {
        // given
        var logger = Substitute.For<ILogger<AmazonSqsQueueTransport>>();
        await using var transport = new AmazonSqsQueueTransport(logger, _CreateOptions());

        var sqsClient = Substitute.For<IAmazonSQS>();
        sqsClient
            .SendMessageAsync(Arg.Any<SendMessageRequest>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new OperationCanceledException());

        _SetSqsClient(transport, sqsClient);
        _SetQueueUrl(transport, "queue-OrderCreated", "https://sqs.local/queue-OrderCreated");

        var message = new TransportMessage(
            headers: new Dictionary<string, string?>(StringComparer.Ordinal) { [Headers.MessageName] = "OrderCreated" },
            body: "test"u8.ToArray()
        );

        // when
        var act = () => transport.SendAsync(message, AbortToken);

        // then
        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    private static void _SetSqsClient(AmazonSqsQueueTransport transport, IAmazonSQS sqsClient)
    {
        var field = typeof(AmazonSqsQueueTransport).GetField(
            "_sqsClient",
            BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.DeclaredOnly
        )!;
        field.SetValue(transport, sqsClient);
    }

    private static void _SetQueueUrl(AmazonSqsQueueTransport transport, string queueName, string queueUrl)
    {
        var field = typeof(AmazonSqsQueueTransport).GetField(
            "_queueUrlMaps",
            BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.DeclaredOnly
        )!;
        var queueUrls = (ConcurrentDictionary<string, string>)field.GetValue(transport)!;
        queueUrls[queueName] = queueUrl;
    }
}
