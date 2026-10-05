// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Collections.Concurrent;
using System.Reflection;
using Amazon.SimpleNotificationService;
using Amazon.SimpleNotificationService.Model;
using Headless.Messaging;
using Headless.Messaging.Aws;
using Headless.Testing.Tests;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NSubstitute.ExceptionExtensions;

namespace Tests;

public sealed class AmazonSnsBusTransportTests : TestBase
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

    [Fact]
    public async Task should_return_failed_result_without_sending_when_sending_after_dispose()
    {
        var transport = new AmazonSnsBusTransport(Substitute.For<ILogger<AmazonSnsBusTransport>>(), _CreateOptions());
        var client = Substitute.For<IAmazonSimpleNotificationService>();
        _SetSnsClient(
            transport,
            client,
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["bus-orders"] = "arn:aws:sns:us-east-1:123456789:bus-orders",
            }
        );
        await transport.DisposeAsync();

        var result = await transport.SendAsync(
            new TransportMessage(
                new Dictionary<string, string?>(StringComparer.Ordinal)
                {
                    [Headers.MessageId] = "message-1",
                    [Headers.MessageName] = "orders",
                },
                "payload"u8.ToArray()
            ),
            AbortToken
        );

        result.Succeeded.Should().BeFalse();
        result.Exception.Should().BeOfType<ObjectDisposedException>();
        await client.DidNotReceive().PublishAsync(Arg.Any<PublishRequest>(), Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData(null)]
    [InlineData("order-42")]
    public async Task should_map_typed_affinity_to_native_sns_fifo_group(string? raw)
    {
        await using var transport = new AmazonSnsBusTransport(
            Substitute.For<ILogger<AmazonSnsBusTransport>>(),
            _CreateOptions()
        );
        var client = Substitute.For<IAmazonSimpleNotificationService>();
        _SetSnsClient(
            transport,
            client,
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["bus-orders.fifo"] = "arn:aws:sns:us-east-1:123456789:bus-orders.fifo",
            }
        );
        client.PublishAsync(Arg.Any<PublishRequest>(), Arg.Any<CancellationToken>()).Returns(new PublishResponse());
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
            .PublishAsync(Arg.Is<PublishRequest>(request => request.MessageGroupId == "order-42"), AbortToken);
    }

    [Fact]
    public async Task should_return_correct_broker_address()
    {
        // given
        var logger = Substitute.For<ILogger<AmazonSnsBusTransport>>();
        await using var transport = new AmazonSnsBusTransport(logger, _CreateOptions());

        // when
        var brokerAddress = transport.BrokerAddress;

        // then
        brokerAddress.Name.Should().Be("aws_sns");
        brokerAddress.Endpoint.Should().Be("localhost:4566");
    }

    [Fact]
    public async Task should_use_region_based_broker_endpoint_when_service_url_not_configured()
    {
        // given
        var logger = Substitute.For<ILogger<AmazonSnsBusTransport>>();
        var options = Options.Create(new AmazonSqsMessagingOptions { Region = Amazon.RegionEndpoint.USEast1 });
        await using var transport = new AmazonSnsBusTransport(logger, options);

        // when
        var brokerAddress = transport.BrokerAddress;

        // then
        brokerAddress.Name.Should().Be("aws_sns");
        brokerAddress.Endpoint.Should().Be("sns.us-east-1.amazonaws.com");
    }

    [Fact]
    public async Task should_use_partition_dns_suffix_for_non_standard_regions()
    {
        // given
        var logger = Substitute.For<ILogger<AmazonSnsBusTransport>>();
        var options = Options.Create(new AmazonSqsMessagingOptions { Region = Amazon.RegionEndpoint.CNNorth1 });
        await using var transport = new AmazonSnsBusTransport(logger, options);

        // when
        var brokerAddress = transport.BrokerAddress;

        // then
        brokerAddress.Name.Should().Be("aws_sns");
        brokerAddress.Endpoint.Should().Be("sns.cn-north-1.amazonaws.com.cn");
    }

    [Fact]
    public async Task should_send_message_to_topic()
    {
        // given
        var logger = Substitute.For<ILogger<AmazonSnsBusTransport>>();
        await using var transport = new AmazonSnsBusTransport(logger, _CreateOptions());

        var snsClient = Substitute.For<IAmazonSimpleNotificationService>();
        snsClient
            .PublishAsync(Arg.Any<PublishRequest>(), Arg.Any<CancellationToken>())
            .Returns(new PublishResponse { MessageId = "msg-123" });

        _SetSnsClient(
            transport,
            snsClient,
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["bus-TestEvent"] = "arn:aws:sns:us-east-1:123456789:bus-TestEvent",
            }
        );

        var message = new TransportMessage(
            headers: new Dictionary<string, string?>(StringComparer.Ordinal)
            {
                [Headers.MessageName] = "TestEvent",
                [Headers.MessageId] = "test-id-123",
            },
            body: """{"data": "test"}"""u8.ToArray()
        );

        // when
        var result = await transport.SendAsync(message, AbortToken);

        // then
        result.Succeeded.Should().BeTrue();
        await snsClient
            .Received(1)
            .PublishAsync(
                Arg.Is<PublishRequest>(r =>
                    r.TopicArn == "arn:aws:sns:us-east-1:123456789:bus-TestEvent" && r.Message == """{"data": "test"}"""
                ),
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task should_include_message_attributes_in_request()
    {
        // given
        var logger = Substitute.For<ILogger<AmazonSnsBusTransport>>();
        await using var transport = new AmazonSnsBusTransport(logger, _CreateOptions());

        var snsClient = Substitute.For<IAmazonSimpleNotificationService>();
        snsClient
            .PublishAsync(Arg.Any<PublishRequest>(), Arg.Any<CancellationToken>())
            .Returns(new PublishResponse { MessageId = "msg-123" });

        _SetSnsClient(
            transport,
            snsClient,
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["bus-TestEvent"] = "arn:aws:sns:us-east-1:123456789:bus-TestEvent",
            }
        );

        var message = new TransportMessage(
            headers: new Dictionary<string, string?>(StringComparer.Ordinal)
            {
                [Headers.MessageName] = "TestEvent",
                [Headers.MessageId] = "test-id-123",
                ["custom-header"] = "custom-value",
            },
            body: "test"u8.ToArray()
        );

        // when
        var result = await transport.SendAsync(message, AbortToken);

        // then
        result.Succeeded.Should().BeTrue();
        await snsClient
            .Received(1)
            .PublishAsync(
                Arg.Is<PublishRequest>(r =>
                    r.MessageAttributes.ContainsKey(Headers.MessageName)
                    && r.MessageAttributes[Headers.MessageName].StringValue == "TestEvent"
                    && r.MessageAttributes.ContainsKey(Headers.MessageId)
                    && r.MessageAttributes[Headers.MessageId].StringValue == "test-id-123"
                    && r.MessageAttributes.ContainsKey("custom-header")
                    && r.MessageAttributes["custom-header"].StringValue == "custom-value"
                ),
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task should_create_topic_if_not_exists()
    {
        // given
        var logger = Substitute.For<ILogger<AmazonSnsBusTransport>>();
        await using var transport = new AmazonSnsBusTransport(logger, _CreateOptions());

        var snsClient = Substitute.For<IAmazonSimpleNotificationService>();
        snsClient
            .CreateTopicAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new CreateTopicResponse { TopicArn = "arn:aws:sns:us-east-1:123456789:bus-NewTopic" });
        snsClient
            .PublishAsync(Arg.Any<PublishRequest>(), Arg.Any<CancellationToken>())
            .Returns(new PublishResponse { MessageId = "msg-123" });

        // Empty topic map forces CreateTopicAsync to be called
        _SetSnsClient(transport, snsClient, []);

        var message = new TransportMessage(
            headers: new Dictionary<string, string?>(StringComparer.Ordinal) { [Headers.MessageName] = "NewTopic" },
            body: "test"u8.ToArray()
        );

        // when
        var result = await transport.SendAsync(message, AbortToken);

        // then
        result.Succeeded.Should().BeTrue();
        await snsClient.Received(1).CreateTopicAsync("bus-NewTopic", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task should_issue_one_create_per_topic_when_first_sends_are_concurrent()
    {
        // given
        var logger = Substitute.For<ILogger<AmazonSnsBusTransport>>();
        await using var transport = new AmazonSnsBusTransport(logger, _CreateOptions());

        // The creates stay pending until every send has started, so the sends overlap on each missing topic.
        var ordersCreated = new TaskCompletionSource<CreateTopicResponse>(
            TaskCreationOptions.RunContinuationsAsynchronously
        );
        var invoicesCreated = new TaskCompletionSource<CreateTopicResponse>(
            TaskCreationOptions.RunContinuationsAsynchronously
        );
        var snsClient = Substitute.For<IAmazonSimpleNotificationService>();
        snsClient.CreateTopicAsync("bus-orders", Arg.Any<CancellationToken>()).Returns(ordersCreated.Task);
        snsClient.CreateTopicAsync("bus-invoices", Arg.Any<CancellationToken>()).Returns(invoicesCreated.Task);
        snsClient
            .PublishAsync(Arg.Any<PublishRequest>(), Arg.Any<CancellationToken>())
            .Returns(new PublishResponse { MessageId = "msg-123" });

        _SetSnsClient(transport, snsClient, []);

        // when
        var sends = Enumerable
            .Range(0, 20)
            .Select(i =>
                transport.SendAsync(
                    new TransportMessage(
                        new Dictionary<string, string?>(StringComparer.Ordinal)
                        {
                            [Headers.MessageName] = i % 2 == 0 ? "orders" : "invoices",
                        },
                        "test"u8.ToArray()
                    ),
                    AbortToken
                )
            )
            .ToArray();

        ordersCreated.SetResult(new CreateTopicResponse { TopicArn = "arn:aws:sns:us-east-1:123456789:bus-orders" });
        invoicesCreated.SetResult(
            new CreateTopicResponse { TopicArn = "arn:aws:sns:us-east-1:123456789:bus-invoices" }
        );
        var results = await Task.WhenAll(sends);

        // then
        results.Should().AllSatisfy(r => r.Succeeded.Should().BeTrue("the send failed with {0}", r.Exception));
        await snsClient.Received(1).CreateTopicAsync("bus-orders", Arg.Any<CancellationToken>());
        await snsClient.Received(1).CreateTopicAsync("bus-invoices", Arg.Any<CancellationToken>());
        await snsClient
            .Received(10)
            .PublishAsync(
                Arg.Is<PublishRequest>(r => r.TopicArn == "arn:aws:sns:us-east-1:123456789:bus-orders"),
                Arg.Any<CancellationToken>()
            );
        await snsClient
            .Received(10)
            .PublishAsync(
                Arg.Is<PublishRequest>(r => r.TopicArn == "arn:aws:sns:us-east-1:123456789:bus-invoices"),
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task should_retry_topic_creation_on_the_next_send_after_a_failed_create()
    {
        // given
        var logger = Substitute.For<ILogger<AmazonSnsBusTransport>>();
        await using var transport = new AmazonSnsBusTransport(logger, _CreateOptions());

        var snsClient = Substitute.For<IAmazonSimpleNotificationService>();
        snsClient
            .CreateTopicAsync("bus-orders", Arg.Any<CancellationToken>())
            .Returns(
                _ => Task.FromException<CreateTopicResponse>(new AmazonSimpleNotificationServiceException("Throttled")),
                _ =>
                    Task.FromResult(new CreateTopicResponse { TopicArn = "arn:aws:sns:us-east-1:123456789:bus-orders" })
            );
        snsClient
            .PublishAsync(Arg.Any<PublishRequest>(), Arg.Any<CancellationToken>())
            .Returns(new PublishResponse { MessageId = "msg-123" });

        _SetSnsClient(transport, snsClient, []);

        var message = new TransportMessage(
            new Dictionary<string, string?>(StringComparer.Ordinal) { [Headers.MessageName] = "orders" },
            "test"u8.ToArray()
        );

        // when
        var first = await transport.SendAsync(message, AbortToken);
        var second = await transport.SendAsync(message, AbortToken);

        // then
        first.Succeeded.Should().BeFalse();
        first.Exception!.Message.Should().Contain("Throttled");
        second.Succeeded.Should().BeTrue("the send failed with {0}", second.Exception);
        await snsClient.Received(2).CreateTopicAsync("bus-orders", Arg.Any<CancellationToken>());
        await snsClient.Received(1).PublishAsync(Arg.Any<PublishRequest>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task should_create_fifo_topic_and_publish_with_fifo_metadata()
    {
        // given
        var logger = Substitute.For<ILogger<AmazonSnsBusTransport>>();
        await using var transport = new AmazonSnsBusTransport(logger, _CreateOptions());
        var expectedTopicName = AwsPhysicalAddress.BusTopic("order.created.fifo");

        var snsClient = Substitute.For<IAmazonSimpleNotificationService>();
        snsClient
            .CreateTopicAsync(Arg.Any<CreateTopicRequest>(), Arg.Any<CancellationToken>())
            .Returns(new CreateTopicResponse { TopicArn = $"arn:aws:sns:us-east-1:123456789:{expectedTopicName}" });
        snsClient
            .PublishAsync(Arg.Any<PublishRequest>(), Arg.Any<CancellationToken>())
            .Returns(new PublishResponse { MessageId = "msg-123" });

        _SetSnsClient(transport, snsClient, []);

        var message = new TransportMessage(
            headers: new Dictionary<string, string?>(StringComparer.Ordinal)
            {
                [Headers.MessageName] = "order.created.fifo",
                [Headers.MessageId] = "message-1",
                [Headers.RoutingAffinityKey] = "tenant-a",
            },
            body: "test"u8.ToArray()
        );

        // when
        var result = await transport.SendAsync(message, AbortToken);

        // then
        result.Succeeded.Should().BeTrue();
        await snsClient
            .Received(1)
            .CreateTopicAsync(
                Arg.Is<CreateTopicRequest>(r =>
                    r.Name == expectedTopicName
                    && r.Attributes["FifoTopic"] == "true"
                    && r.Attributes["ContentBasedDeduplication"] == "true"
                ),
                Arg.Any<CancellationToken>()
            );
        await snsClient
            .Received(1)
            .PublishAsync(
                Arg.Is<PublishRequest>(r => r.MessageGroupId == "tenant-a" && r.MessageDeduplicationId == "message-1"),
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task should_use_default_message_group_for_fifo_topic_without_affinity_key()
    {
        // given
        var logger = Substitute.For<ILogger<AmazonSnsBusTransport>>();
        await using var transport = new AmazonSnsBusTransport(logger, _CreateOptions());

        var snsClient = Substitute.For<IAmazonSimpleNotificationService>();
        snsClient
            .CreateTopicAsync(Arg.Any<CreateTopicRequest>(), Arg.Any<CancellationToken>())
            .Returns(new CreateTopicResponse { TopicArn = "arn:aws:sns:us-east-1:123456789:bus-order-created.fifo" });
        snsClient
            .PublishAsync(Arg.Any<PublishRequest>(), Arg.Any<CancellationToken>())
            .Returns(new PublishResponse { MessageId = "msg-123" });

        _SetSnsClient(transport, snsClient, []);

        var message = new TransportMessage(
            headers: new Dictionary<string, string?>(StringComparer.Ordinal)
            {
                [Headers.MessageName] = "order.created.fifo",
                [Headers.MessageId] = "message-1",
                [Headers.ConsumerIdentity] = "billing.invoice-projection",
            },
            body: "test"u8.ToArray()
        );

        // when
        var result = await transport.SendAsync(message, AbortToken);

        // then — a consumer identity header is not an ordering key
        result.Succeeded.Should().BeTrue();
        await snsClient
            .Received(1)
            .PublishAsync(Arg.Is<PublishRequest>(r => r.MessageGroupId == "default"), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task should_prefer_explicit_message_group_id_header_for_fifo_topic()
    {
        // given
        var logger = Substitute.For<ILogger<AmazonSnsBusTransport>>();
        await using var transport = new AmazonSnsBusTransport(logger, _CreateOptions());

        var snsClient = Substitute.For<IAmazonSimpleNotificationService>();
        snsClient
            .CreateTopicAsync(Arg.Any<CreateTopicRequest>(), Arg.Any<CancellationToken>())
            .Returns(new CreateTopicResponse { TopicArn = "arn:aws:sns:us-east-1:123456789:bus-order-created.fifo" });
        snsClient
            .PublishAsync(Arg.Any<PublishRequest>(), Arg.Any<CancellationToken>())
            .Returns(new PublishResponse { MessageId = "msg-123" });

        _SetSnsClient(transport, snsClient, []);

        var message = new TransportMessage(
            headers: new Dictionary<string, string?>(StringComparer.Ordinal)
            {
                [Headers.MessageName] = "order.created.fifo",
                [Headers.MessageId] = "message-1",
                [AwsMessagingHeaders.MessageGroupId] = "tenant-b",
            },
            body: "test"u8.ToArray()
        );

        // when
        var result = await transport.SendAsync(message, AbortToken);

        // then
        result.Succeeded.Should().BeTrue();
        await snsClient
            .Received(1)
            .PublishAsync(Arg.Is<PublishRequest>(r => r.MessageGroupId == "tenant-b"), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task should_handle_send_failure()
    {
        // given
        var logger = Substitute.For<ILogger<AmazonSnsBusTransport>>();
        await using var transport = new AmazonSnsBusTransport(logger, _CreateOptions());

        var snsClient = Substitute.For<IAmazonSimpleNotificationService>();
        snsClient
            .PublishAsync(Arg.Any<PublishRequest>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new AmazonSimpleNotificationServiceException("Network error"));

        _SetSnsClient(
            transport,
            snsClient,
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["bus-TestEvent"] = "arn:aws:sns:us-east-1:123456789:bus-TestEvent",
            }
        );

        var message = new TransportMessage(
            headers: new Dictionary<string, string?>(StringComparer.Ordinal) { [Headers.MessageName] = "TestEvent" },
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
    public async Task should_return_failed_when_topic_not_found_and_creation_fails()
    {
        // given
        var logger = Substitute.For<ILogger<AmazonSnsBusTransport>>();
        await using var transport = new AmazonSnsBusTransport(logger, _CreateOptions());

        var snsClient = Substitute.For<IAmazonSimpleNotificationService>();
        snsClient
            .CreateTopicAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new CreateTopicResponse { TopicArn = string.Empty }); // Empty ARN indicates failure

        // Empty topic map forces CreateTopicAsync to be called
        _SetSnsClient(transport, snsClient, []);

        var message = new TransportMessage(
            headers: new Dictionary<string, string?>(StringComparer.Ordinal) { [Headers.MessageName] = "NonExistent" },
            body: "test"u8.ToArray()
        );

        // when
        var result = await transport.SendAsync(message, AbortToken);

        // then
        result.Succeeded.Should().BeFalse();
    }

    [Fact]
    public async Task should_normalize_topic_name_for_aws()
    {
        // given
        var logger = Substitute.For<ILogger<AmazonSnsBusTransport>>();
        await using var transport = new AmazonSnsBusTransport(logger, _CreateOptions());
        var expectedTopicName = AwsPhysicalAddress.BusTopic("my.topic:name");

        var snsClient = Substitute.For<IAmazonSimpleNotificationService>();
        snsClient
            .CreateTopicAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new CreateTopicResponse { TopicArn = $"arn:aws:sns:us-east-1:123456789:{expectedTopicName}" });
        snsClient
            .PublishAsync(Arg.Any<PublishRequest>(), Arg.Any<CancellationToken>())
            .Returns(new PublishResponse { MessageId = "msg-123" });

        // Empty topic map forces CreateTopicAsync to be called
        _SetSnsClient(transport, snsClient, []);

        // Topic name with dots and colons should be normalized
        var message = new TransportMessage(
            headers: new Dictionary<string, string?>(StringComparer.Ordinal)
            {
                [Headers.MessageName] = "my.topic:name",
            },
            body: "test"u8.ToArray()
        );

        // when
        var result = await transport.SendAsync(message, AbortToken);

        // then
        result.Succeeded.Should().BeTrue();
        // Normalized characters retain a stable discriminator so distinct logical names remain distinct.
        await snsClient.Received(1).CreateTopicAsync(expectedTopicName, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task should_handle_empty_message_body()
    {
        // given
        var logger = Substitute.For<ILogger<AmazonSnsBusTransport>>();
        await using var transport = new AmazonSnsBusTransport(logger, _CreateOptions());

        var snsClient = Substitute.For<IAmazonSimpleNotificationService>();
        snsClient
            .PublishAsync(Arg.Any<PublishRequest>(), Arg.Any<CancellationToken>())
            .Returns(new PublishResponse { MessageId = "msg-123" });

        _SetSnsClient(
            transport,
            snsClient,
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["bus-TestEvent"] = "arn:aws:sns:us-east-1:123456789:bus-TestEvent",
            }
        );

        var message = new TransportMessage(
            headers: new Dictionary<string, string?>(StringComparer.Ordinal) { [Headers.MessageName] = "TestEvent" },
            body: ReadOnlyMemory<byte>.Empty
        );

        // when
        var result = await transport.SendAsync(message, AbortToken);

        // then
        result.Succeeded.Should().BeTrue();
        await snsClient
            .Received(1)
            .PublishAsync(Arg.Is<PublishRequest>(r => r.Message == string.Empty), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task should_cache_topic_arns_after_first_fetch()
    {
        // given
        var logger = Substitute.For<ILogger<AmazonSnsBusTransport>>();
        await using var transport = new AmazonSnsBusTransport(logger, _CreateOptions());

        var snsClient = Substitute.For<IAmazonSimpleNotificationService>();
        snsClient
            .PublishAsync(Arg.Any<PublishRequest>(), Arg.Any<CancellationToken>())
            .Returns(new PublishResponse { MessageId = "msg-123" });

        _SetSnsClient(
            transport,
            snsClient,
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["bus-TestEvent"] = "arn:aws:sns:us-east-1:123456789:bus-TestEvent",
            }
        );

        var message = new TransportMessage(
            headers: new Dictionary<string, string?>(StringComparer.Ordinal) { [Headers.MessageName] = "TestEvent" },
            body: "test"u8.ToArray()
        );

        // when - send multiple messages
        await transport.SendAsync(message, AbortToken);
        await transport.SendAsync(message, AbortToken);
        await transport.SendAsync(message, AbortToken);

        // then - _topicArnMaps is pre-populated, so ListTopicsAsync should never be called
        await snsClient.DidNotReceive().ListTopicsAsync(Arg.Any<string?>(), Arg.Any<CancellationToken>());

        // All three messages should be published successfully
        await snsClient.Received(3).PublishAsync(Arg.Any<PublishRequest>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task should_skip_null_header_values()
    {
        // given
        var logger = Substitute.For<ILogger<AmazonSnsBusTransport>>();
        await using var transport = new AmazonSnsBusTransport(logger, _CreateOptions());

        var snsClient = Substitute.For<IAmazonSimpleNotificationService>();
        snsClient
            .PublishAsync(Arg.Any<PublishRequest>(), Arg.Any<CancellationToken>())
            .Returns(new PublishResponse { MessageId = "msg-123" });

        _SetSnsClient(
            transport,
            snsClient,
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["bus-TestEvent"] = "arn:aws:sns:us-east-1:123456789:bus-TestEvent",
            }
        );

        var message = new TransportMessage(
            headers: new Dictionary<string, string?>(StringComparer.Ordinal)
            {
                [Headers.MessageName] = "TestEvent",
                ["null-header"] = null, // This should be skipped
                ["valid-header"] = "value",
            },
            body: "test"u8.ToArray()
        );

        // when
        var result = await transport.SendAsync(message, AbortToken);

        // then
        result.Succeeded.Should().BeTrue();
        await snsClient
            .Received(1)
            .PublishAsync(
                Arg.Is<PublishRequest>(r =>
                    !r.MessageAttributes.ContainsKey("null-header") && r.MessageAttributes.ContainsKey("valid-header")
                ),
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task should_dispose_resources()
    {
        // given
        var logger = Substitute.For<ILogger<AmazonSnsBusTransport>>();
        var transport = new AmazonSnsBusTransport(logger, _CreateOptions());

        var snsClient = Substitute.For<IAmazonSimpleNotificationService>();
        _SetSnsClient(transport, snsClient);

        // when
        await transport.DisposeAsync();

        // then
        snsClient.Received(1).Dispose();
    }

    [Fact]
    public async Task should_create_topic_when_the_account_lists_no_topics()
    {
        // given
        var logger = Substitute.For<ILogger<AmazonSnsBusTransport>>();
        await using var transport = new AmazonSnsBusTransport(logger, _CreateOptions());

        var snsClient = Substitute.For<IAmazonSimpleNotificationService>();
        // The SDK leaves Topics null when the listing has no topics.
        snsClient.ListTopicsAsync(Arg.Any<CancellationToken>()).Returns(new ListTopicsResponse { Topics = null });
        snsClient
            .CreateTopicAsync("bus-orders", Arg.Any<CancellationToken>())
            .Returns(new CreateTopicResponse { TopicArn = "arn:aws:sns:us-east-1:123456789:bus-orders" });
        snsClient
            .PublishAsync(Arg.Any<PublishRequest>(), Arg.Any<CancellationToken>())
            .Returns(new PublishResponse { MessageId = "msg-123" });

        _SetSnsClientWithoutTopicCache(transport, snsClient);

        var message = new TransportMessage(
            new Dictionary<string, string?>(StringComparer.Ordinal) { [Headers.MessageName] = "orders" },
            "test"u8.ToArray()
        );

        // when
        var result = await transport.SendAsync(message, AbortToken);

        // then
        result.Succeeded.Should().BeTrue("the send failed with {0}", result.Exception);
        await snsClient.Received(1).CreateTopicAsync("bus-orders", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task should_list_topics_again_on_the_next_send_after_a_failed_listing()
    {
        // given
        var logger = Substitute.For<ILogger<AmazonSnsBusTransport>>();
        await using var transport = new AmazonSnsBusTransport(logger, _CreateOptions());

        var snsClient = Substitute.For<IAmazonSimpleNotificationService>();
        snsClient
            .ListTopicsAsync(Arg.Any<CancellationToken>())
            .Returns(
                _ => Task.FromException<ListTopicsResponse>(new AmazonSimpleNotificationServiceException("Throttled")),
                _ =>
                    Task.FromResult(
                        new ListTopicsResponse
                        {
                            Topics = [new Topic { TopicArn = "arn:aws:sns:us-east-1:123456789:bus-orders" }],
                        }
                    )
            );
        snsClient
            .PublishAsync(Arg.Any<PublishRequest>(), Arg.Any<CancellationToken>())
            .Returns(new PublishResponse { MessageId = "msg-123" });

        _SetSnsClientWithoutTopicCache(transport, snsClient);

        var message = new TransportMessage(
            new Dictionary<string, string?>(StringComparer.Ordinal) { [Headers.MessageName] = "orders" },
            "test"u8.ToArray()
        );

        // when
        var first = await transport.SendAsync(message, AbortToken);
        var second = await transport.SendAsync(message, AbortToken);

        // then - the second send uses the listed topic instead of a partial cache from the failed listing
        first.Succeeded.Should().BeFalse();
        second.Succeeded.Should().BeTrue("the send failed with {0}", second.Exception);
        await snsClient.Received(2).ListTopicsAsync(Arg.Any<CancellationToken>());
        await snsClient.DidNotReceive().CreateTopicAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
        await snsClient
            .Received(1)
            .PublishAsync(
                Arg.Is<PublishRequest>(r => r.TopicArn == "arn:aws:sns:us-east-1:123456789:bus-orders"),
                Arg.Any<CancellationToken>()
            );
    }

    private static void _SetSnsClientWithoutTopicCache(
        AmazonSnsBusTransport transport,
        IAmazonSimpleNotificationService snsClient
    )
    {
        typeof(AmazonSnsBusTransport)
            .GetField("_snsClient", BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.DeclaredOnly)!
            .SetValue(transport, snsClient);
    }

    private static void _SetSnsClient(
        AmazonSnsBusTransport transport,
        IAmazonSimpleNotificationService snsClient,
        Dictionary<string, string>? topicArnMaps = null
    )
    {
        var snsClientField = typeof(AmazonSnsBusTransport).GetField(
            "_snsClient",
            BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.DeclaredOnly
        );
        snsClientField!.SetValue(transport, snsClient);

        // Must also set _topicArnMaps to prevent _FetchExistingTopicArns from overwriting the mock
        var topicArnMapsField = typeof(AmazonSnsBusTransport).GetField(
            "_topicArnMaps",
            BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.DeclaredOnly
        );
        topicArnMapsField!.SetValue(
            transport,
            new ConcurrentDictionary<string, string>(topicArnMaps ?? [], StringComparer.Ordinal)
        );
    }
}
