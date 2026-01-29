// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Reflection;
using Amazon.SimpleNotificationService;
using Amazon.SimpleNotificationService.Model;
using Headless.Messaging.AwsSqs;
using Headless.Messaging.Messages;
using Headless.Testing.Tests;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NSubstitute.ExceptionExtensions;

namespace Tests;

public sealed class AmazonSqsTransportTests : TestBase
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
        var logger = Substitute.For<ILogger<AmazonSqsTransport>>();
        await using var transport = new AmazonSqsTransport(logger, _CreateOptions());

        // when
        var brokerAddress = transport.BrokerAddress;

        // then
        brokerAddress.Name.Should().Be("AmazonSQS");
    }

    [Fact]
    public async Task should_send_message_to_topic()
    {
        // given
        var logger = Substitute.For<ILogger<AmazonSqsTransport>>();
        await using var transport = new AmazonSqsTransport(logger, _CreateOptions());

        var snsClient = Substitute.For<IAmazonSimpleNotificationService>();
        snsClient
            .ListTopicsAsync(Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(
                new ListTopicsResponse
                {
                    Topics = [new Topic { TopicArn = "arn:aws:sns:us-east-1:123456789:TestEvent" }],
                }
            );
        snsClient
            .PublishAsync(Arg.Any<PublishRequest>(), Arg.Any<CancellationToken>())
            .Returns(new PublishResponse { MessageId = "msg-123" });

        _SetSnsClient(transport, snsClient);

        var message = new TransportMessage(
            headers: new Dictionary<string, string?>(StringComparer.Ordinal)
            {
                [Headers.MessageName] = "TestEvent",
                [Headers.MessageId] = "test-id-123",
            },
            body: """{"data": "test"}"""u8.ToArray()
        );

        // when
        var result = await transport.SendAsync(message);

        // then
        result.Succeeded.Should().BeTrue();
        await snsClient
            .Received(1)
            .PublishAsync(
                Arg.Is<PublishRequest>(r =>
                    r.TopicArn == "arn:aws:sns:us-east-1:123456789:TestEvent" && r.Message == """{"data": "test"}"""
                ),
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task should_include_message_attributes_in_request()
    {
        // given
        var logger = Substitute.For<ILogger<AmazonSqsTransport>>();
        await using var transport = new AmazonSqsTransport(logger, _CreateOptions());

        var snsClient = Substitute.For<IAmazonSimpleNotificationService>();
        snsClient
            .ListTopicsAsync(Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(
                new ListTopicsResponse
                {
                    Topics = [new Topic { TopicArn = "arn:aws:sns:us-east-1:123456789:TestEvent" }],
                }
            );
        snsClient
            .PublishAsync(Arg.Any<PublishRequest>(), Arg.Any<CancellationToken>())
            .Returns(new PublishResponse { MessageId = "msg-123" });

        _SetSnsClient(transport, snsClient);

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
        var result = await transport.SendAsync(message);

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
        var logger = Substitute.For<ILogger<AmazonSqsTransport>>();
        await using var transport = new AmazonSqsTransport(logger, _CreateOptions());

        var snsClient = Substitute.For<IAmazonSimpleNotificationService>();
        snsClient
            .ListTopicsAsync(Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(new ListTopicsResponse { Topics = [] });
        snsClient
            .CreateTopicAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new CreateTopicResponse { TopicArn = "arn:aws:sns:us-east-1:123456789:NewTopic" });
        snsClient
            .PublishAsync(Arg.Any<PublishRequest>(), Arg.Any<CancellationToken>())
            .Returns(new PublishResponse { MessageId = "msg-123" });

        _SetSnsClient(transport, snsClient);

        var message = new TransportMessage(
            headers: new Dictionary<string, string?>(StringComparer.Ordinal) { [Headers.MessageName] = "NewTopic" },
            body: "test"u8.ToArray()
        );

        // when
        var result = await transport.SendAsync(message);

        // then
        result.Succeeded.Should().BeTrue();
        await snsClient.Received(1).CreateTopicAsync("NewTopic", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task should_handle_send_failure()
    {
        // given
        var logger = Substitute.For<ILogger<AmazonSqsTransport>>();
        await using var transport = new AmazonSqsTransport(logger, _CreateOptions());

        var snsClient = Substitute.For<IAmazonSimpleNotificationService>();
        snsClient
            .ListTopicsAsync(Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(
                new ListTopicsResponse
                {
                    Topics = [new Topic { TopicArn = "arn:aws:sns:us-east-1:123456789:TestEvent" }],
                }
            );
        snsClient
            .PublishAsync(Arg.Any<PublishRequest>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new AmazonSimpleNotificationServiceException("Network error"));

        _SetSnsClient(transport, snsClient);

        var message = new TransportMessage(
            headers: new Dictionary<string, string?>(StringComparer.Ordinal) { [Headers.MessageName] = "TestEvent" },
            body: "test"u8.ToArray()
        );

        // when
        var result = await transport.SendAsync(message);

        // then
        result.Succeeded.Should().BeFalse();
        result.Exception.Should().NotBeNull();
        result.Exception!.Message.Should().Contain("Network error");
    }

    [Fact]
    public async Task should_return_failed_when_topic_not_found_and_creation_fails()
    {
        // given
        var logger = Substitute.For<ILogger<AmazonSqsTransport>>();
        await using var transport = new AmazonSqsTransport(logger, _CreateOptions());

        var snsClient = Substitute.For<IAmazonSimpleNotificationService>();
        snsClient
            .ListTopicsAsync(Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(new ListTopicsResponse { Topics = [] });
        snsClient
            .CreateTopicAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new CreateTopicResponse { TopicArn = string.Empty }); // Empty ARN indicates failure

        _SetSnsClient(transport, snsClient);

        var message = new TransportMessage(
            headers: new Dictionary<string, string?>(StringComparer.Ordinal) { [Headers.MessageName] = "NonExistent" },
            body: "test"u8.ToArray()
        );

        // when
        var result = await transport.SendAsync(message);

        // then
        result.Succeeded.Should().BeFalse();
    }

    [Fact]
    public async Task should_normalize_topic_name_for_aws()
    {
        // given
        var logger = Substitute.For<ILogger<AmazonSqsTransport>>();
        await using var transport = new AmazonSqsTransport(logger, _CreateOptions());

        var snsClient = Substitute.For<IAmazonSimpleNotificationService>();
        snsClient
            .ListTopicsAsync(Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(new ListTopicsResponse { Topics = [] });
        snsClient
            .CreateTopicAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new CreateTopicResponse { TopicArn = "arn:aws:sns:us-east-1:123456789:my-topic_name" });
        snsClient
            .PublishAsync(Arg.Any<PublishRequest>(), Arg.Any<CancellationToken>())
            .Returns(new PublishResponse { MessageId = "msg-123" });

        _SetSnsClient(transport, snsClient);

        // Topic name with dots and colons should be normalized
        var message = new TransportMessage(
            headers: new Dictionary<string, string?>(StringComparer.Ordinal)
            {
                [Headers.MessageName] = "my.topic:name",
            },
            body: "test"u8.ToArray()
        );

        // when
        var result = await transport.SendAsync(message);

        // then
        result.Succeeded.Should().BeTrue();
        // Dots become dashes, colons become underscores
        await snsClient.Received(1).CreateTopicAsync("my-topic_name", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task should_handle_empty_message_body()
    {
        // given
        var logger = Substitute.For<ILogger<AmazonSqsTransport>>();
        await using var transport = new AmazonSqsTransport(logger, _CreateOptions());

        var snsClient = Substitute.For<IAmazonSimpleNotificationService>();
        snsClient
            .ListTopicsAsync(Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(
                new ListTopicsResponse
                {
                    Topics = [new Topic { TopicArn = "arn:aws:sns:us-east-1:123456789:TestEvent" }],
                }
            );
        snsClient
            .PublishAsync(Arg.Any<PublishRequest>(), Arg.Any<CancellationToken>())
            .Returns(new PublishResponse { MessageId = "msg-123" });

        _SetSnsClient(transport, snsClient);

        var message = new TransportMessage(
            headers: new Dictionary<string, string?>(StringComparer.Ordinal) { [Headers.MessageName] = "TestEvent" },
            body: ReadOnlyMemory<byte>.Empty
        );

        // when
        var result = await transport.SendAsync(message);

        // then
        result.Succeeded.Should().BeTrue();
        await snsClient
            .Received(1)
            .PublishAsync(Arg.Is<PublishRequest>(r => r.Message == null), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task should_cache_topic_arns_after_first_fetch()
    {
        // given
        var logger = Substitute.For<ILogger<AmazonSqsTransport>>();
        await using var transport = new AmazonSqsTransport(logger, _CreateOptions());

        var snsClient = Substitute.For<IAmazonSimpleNotificationService>();
        snsClient
            .ListTopicsAsync(Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(
                new ListTopicsResponse
                {
                    Topics = [new Topic { TopicArn = "arn:aws:sns:us-east-1:123456789:TestEvent" }],
                }
            );
        snsClient
            .PublishAsync(Arg.Any<PublishRequest>(), Arg.Any<CancellationToken>())
            .Returns(new PublishResponse { MessageId = "msg-123" });

        _SetSnsClient(transport, snsClient);

        var message = new TransportMessage(
            headers: new Dictionary<string, string?>(StringComparer.Ordinal) { [Headers.MessageName] = "TestEvent" },
            body: "test"u8.ToArray()
        );

        // when - send multiple messages
        await transport.SendAsync(message);
        await transport.SendAsync(message);
        await transport.SendAsync(message);

        // then - ListTopicsAsync should only be called once due to caching
        await snsClient.Received(1).ListTopicsAsync(Arg.Any<string?>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task should_skip_null_header_values()
    {
        // given
        var logger = Substitute.For<ILogger<AmazonSqsTransport>>();
        await using var transport = new AmazonSqsTransport(logger, _CreateOptions());

        var snsClient = Substitute.For<IAmazonSimpleNotificationService>();
        snsClient
            .ListTopicsAsync(Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(
                new ListTopicsResponse
                {
                    Topics = [new Topic { TopicArn = "arn:aws:sns:us-east-1:123456789:TestEvent" }],
                }
            );
        snsClient
            .PublishAsync(Arg.Any<PublishRequest>(), Arg.Any<CancellationToken>())
            .Returns(new PublishResponse { MessageId = "msg-123" });

        _SetSnsClient(transport, snsClient);

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
        var result = await transport.SendAsync(message);

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
        var logger = Substitute.For<ILogger<AmazonSqsTransport>>();
        var transport = new AmazonSqsTransport(logger, _CreateOptions());

        var snsClient = Substitute.For<IAmazonSimpleNotificationService>();
        _SetSnsClient(transport, snsClient);

        // when
        await transport.DisposeAsync();

        // then
        snsClient.Received(1).Dispose();
    }

    private static void _SetSnsClient(AmazonSqsTransport transport, IAmazonSimpleNotificationService snsClient)
    {
        var snsClientField = typeof(AmazonSqsTransport).GetField(
            "_snsClient",
            BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.DeclaredOnly
        );
        snsClientField!.SetValue(transport, snsClient);
    }
}
