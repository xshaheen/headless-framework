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
    private static IOptions<AmazonSqsOptions> _CreateOptions() =>
        Options.Create(
            new AmazonSqsOptions
            {
                Region = Amazon.RegionEndpoint.USEast1,
                SqsServiceUrl = "http://localhost:4566",
                SnsServiceUrl = "http://localhost:4566",
            }
        );

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
        _SetQueueUrl(transport, "OrderCreated", "https://sqs.local/OrderCreated");

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
                    r.QueueUrl == "https://sqs.local/OrderCreated"
                    && r.MessageBody == """{"id":42}"""
                    && r.MessageAttributes[Headers.MessageId].StringValue == "message-1"
                    && r.MessageAttributes["custom-header"].StringValue == "custom-value"
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

        var sqsClient = Substitute.For<IAmazonSQS>();
        sqsClient
            .CreateQueueAsync("order-created", Arg.Any<CancellationToken>())
            .Returns(new CreateQueueResponse { QueueUrl = "https://sqs.local/order-created" });
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
        await sqsClient.Received(1).CreateQueueAsync("order-created", Arg.Any<CancellationToken>());
        await sqsClient
            .Received(1)
            .SendMessageAsync(
                Arg.Is<SendMessageRequest>(r => r.QueueUrl == "https://sqs.local/order-created"),
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task should_create_fifo_queue_with_fifo_attributes_when_url_not_cached()
    {
        // given
        var logger = Substitute.For<ILogger<AmazonSqsQueueTransport>>();
        await using var transport = new AmazonSqsQueueTransport(logger, _CreateOptions());

        var sqsClient = Substitute.For<IAmazonSQS>();
        sqsClient
            .CreateQueueAsync(Arg.Any<CreateQueueRequest>(), Arg.Any<CancellationToken>())
            .Returns(new CreateQueueResponse { QueueUrl = "https://sqs.local/order-created.fifo" });
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
                    r.QueueName == "order-created.fifo"
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
            .Returns(new CreateQueueResponse { QueueUrl = "https://sqs.local/order-created.fifo" });
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
    public async Task should_return_failed_when_headers_exceed_sqs_attribute_limit()
    {
        // given
        var logger = Substitute.For<ILogger<AmazonSqsQueueTransport>>();
        await using var transport = new AmazonSqsQueueTransport(logger, _CreateOptions());

        var sqsClient = Substitute.For<IAmazonSQS>();
        _SetSqsClient(transport, sqsClient);
        _SetQueueUrl(transport, "OrderCreated", "https://sqs.local/OrderCreated");

        var headers = Enumerable
            .Range(0, 11)
            .ToDictionary(index => $"header-{index}", index => (string?)$"value-{index}", StringComparer.Ordinal);
        headers[Headers.MessageName] = "OrderCreated";

        var message = new TransportMessage(headers, "test"u8.ToArray());

        // when
        var result = await transport.SendAsync(message, AbortToken);

        // then
        result.Succeeded.Should().BeFalse();
        result.ToString().Should().Contain("AWS_SQS_MESSAGE_ATTRIBUTES_LIMIT");
        await sqsClient.DidNotReceive().SendMessageAsync(Arg.Any<SendMessageRequest>(), Arg.Any<CancellationToken>());
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
        _SetQueueUrl(transport, "OrderCreated", "https://sqs.local/OrderCreated");

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
        _SetQueueUrl(transport, "OrderCreated", "https://sqs.local/OrderCreated");

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
