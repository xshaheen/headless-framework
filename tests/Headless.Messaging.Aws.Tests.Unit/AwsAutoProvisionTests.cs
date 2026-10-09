// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Collections.Concurrent;
using System.Reflection;
using Amazon.SimpleNotificationService;
using Amazon.SimpleNotificationService.Model;
using Amazon.SQS;
using Amazon.SQS.Model;
using Headless.Messaging;
using Headless.Messaging.Aws;
using Headless.Testing.Tests;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NSubstitute.ExceptionExtensions;

namespace Tests;

/// <summary>
/// With <see cref="AmazonSqsMessagingOptions.AutoProvision"/> off, publishers and consumers only look entities up, so the
/// runtime identity needs no create or policy permissions; with it on, Bus subscriptions use raw message delivery.
/// </summary>
public sealed class AwsAutoProvisionTests : TestBase
{
    private readonly IAmazonSQS _sqs = Substitute.For<IAmazonSQS>();
    private readonly IAmazonSimpleNotificationService _sns = Substitute.For<IAmazonSimpleNotificationService>();

    [Fact]
    public async Task should_look_up_a_queue_lane_queue_instead_of_creating_it()
    {
        // given
        _sqs.GetQueueUrlAsync("queue-orders", Arg.Any<CancellationToken>())
            .Returns(new GetQueueUrlResponse { QueueUrl = "https://sqs.local/queue-orders" });
        await using var client = _CreateConsumer(MessageLane.Queue, autoProvision: false);

        // when
        var queueUrls = await client.FetchMessageNamesAsync(["orders"], AbortToken);

        // then
        queueUrls.Should().Equal("https://sqs.local/queue-orders");
        await _sqs.DidNotReceiveWithAnyArgs().CreateQueueAsync(default(CreateQueueRequest)!, AbortToken);
    }

    [Fact]
    public async Task should_name_the_missing_queue_and_the_lookup_permission_when_lookup_fails()
    {
        // given
        _sqs.GetQueueUrlAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(
                new QueueDoesNotExistException("missing") { ErrorCode = "AWS.SimpleQueueService.NonExistentQueue" }
            );
        await using var client = _CreateConsumer(MessageLane.Queue, autoProvision: false);

        // when
        var act = async () => await client.FetchMessageNamesAsync(["orders"], AbortToken);

        // then
        await act.Should()
            .ThrowAsync<InvalidOperationException>()
            .WithMessage("*NonExistentQueue*sqs:GetQueueUrl*AutoProvision is off*");
    }

    [Fact]
    public async Task should_leave_bus_topology_alone_when_it_is_managed_externally()
    {
        // given
        await using var client = _CreateConsumer(MessageLane.Bus, autoProvision: false);

        // when
        var names = await client.FetchMessageNamesAsync(["orders.placed"], AbortToken);
        await client.SubscribeAsync(names, AbortToken);

        // then
        names.Should().Equal("orders.placed");
        _sns.ReceivedCalls().Should().BeEmpty();
        await _sqs.DidNotReceiveWithAnyArgs().SetAttributesAsync(default!, default!);
        await client.WaitUntilReadyAsync(AbortToken);
    }

    [Fact]
    public async Task should_subscribe_bus_queues_with_raw_message_delivery()
    {
        // given
        _GivenBusQueue();
        _sns.SubscribeAsync(Arg.Any<SubscribeRequest>(), Arg.Any<CancellationToken>())
            .Returns(new SubscribeResponse { SubscriptionArn = "arn:aws:sns:::bus-orders-placed:sub" });
        await using var client = _CreateConsumer(MessageLane.Bus, autoProvision: true);

        // when
        await client.SubscribeAsync(["arn:aws:sns:::bus-orders-placed"], AbortToken);

        // then: asked for at creation, and set again because SNS returns an existing subscription unchanged
        await _sns.Received(1)
            .SubscribeAsync(
                Arg.Is<SubscribeRequest>(r =>
                    r.TopicArn == "arn:aws:sns:::bus-orders-placed"
                    && r.Endpoint == "arn:aws:sqs:::bus-orders"
                    && r.Attributes["RawMessageDelivery"] == "true"
                ),
                Arg.Any<CancellationToken>()
            );
        await _sns.Received(1)
            .SetSubscriptionAttributesAsync(
                "arn:aws:sns:::bus-orders-placed:sub",
                "RawMessageDelivery",
                "true",
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task should_switch_an_existing_wrapped_subscription_to_raw_delivery()
    {
        // given: SNS refuses to resubscribe with different attributes
        _GivenBusQueue();
        _sns.SubscribeAsync(Arg.Is<SubscribeRequest>(r => r.Attributes != null), Arg.Any<CancellationToken>())
            .ThrowsAsync(new InvalidParameterException("Subscription already exists with different attributes"));
        _sns.SubscribeAsync(Arg.Is<SubscribeRequest>(r => r.Attributes == null), Arg.Any<CancellationToken>())
            .Returns(new SubscribeResponse { SubscriptionArn = "arn:aws:sns:::bus-orders-placed:old" });
        await using var client = _CreateConsumer(MessageLane.Bus, autoProvision: true);

        // when
        await client.SubscribeAsync(["arn:aws:sns:::bus-orders-placed"], AbortToken);

        // then
        await _sns.Received(1)
            .SetSubscriptionAttributesAsync(
                "arn:aws:sns:::bus-orders-placed:old",
                "RawMessageDelivery",
                "true",
                Arg.Any<CancellationToken>()
            );
    }

    private void _GivenBusQueue()
    {
        _sqs.GetAttributesAsync("https://sqs.local/bus-orders")
            .Returns(
                new Dictionary<string, string>(StringComparer.Ordinal) { ["QueueArn"] = "arn:aws:sqs:::bus-orders" }
            );
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task should_look_up_an_unlisted_topic_instead_of_creating_it(bool exists)
    {
        // given: the topic is missing from the startup listing
        _sns.FindTopicAsync("bus-orders").Returns(exists ? new Topic { TopicArn = "arn:aws:sns:::bus-orders" } : null);
        await using var transport = new AmazonSnsBusTransport(
            Substitute.For<ILogger<AmazonSnsBusTransport>>(),
            _Options(autoProvision: false)
        );
        _SetField(transport, "_snsClient", _sns);
        _SetField(transport, "_topicArnMaps", new ConcurrentDictionary<string, string>(StringComparer.Ordinal));

        // when
        var result = await transport.SendAsync(_Message("orders"), AbortToken);

        // then
        result.Succeeded.Should().Be(exists);
        await _sns.DidNotReceiveWithAnyArgs().CreateTopicAsync(default(string)!, AbortToken);
        await _sns.DidNotReceiveWithAnyArgs().CreateTopicAsync(default(CreateTopicRequest)!, AbortToken);
    }

    [Fact]
    public async Task should_look_up_a_queue_destination_instead_of_creating_it()
    {
        // given
        _sqs.GetQueueUrlAsync("queue-orders", Arg.Any<CancellationToken>())
            .Returns(new GetQueueUrlResponse { QueueUrl = "https://sqs.local/queue-orders" });
        await using var transport = new AmazonSqsQueueTransport(
            Substitute.For<ILogger<AmazonSqsQueueTransport>>(),
            _Options(autoProvision: false)
        );
        _SetField(transport, "_sqsClient", _sqs);

        // when
        var result = await transport.SendAsync(_Message("orders"), AbortToken);

        // then
        result.Succeeded.Should().BeTrue();
        await _sqs.Received(1)
            .SendMessageAsync(
                Arg.Is<SendMessageRequest>(r => r.QueueUrl == "https://sqs.local/queue-orders"),
                Arg.Any<CancellationToken>()
            );
        await _sqs.DidNotReceiveWithAnyArgs().CreateQueueAsync(default(string)!, AbortToken);
        await _sqs.DidNotReceiveWithAnyArgs().CreateQueueAsync(default(CreateQueueRequest)!, AbortToken);
    }

    private AmazonSqsConsumerClient _CreateConsumer(MessageLane lane, bool autoProvision)
    {
        var client = new AmazonSqsConsumerClient(
            "orders",
            1,
            _Options(autoProvision),
            Substitute.For<ILogger<AmazonSqsConsumerClient>>(),
            lane
        );
        _SetField(client, "_sqsClient", _sqs);
        _SetField(client, "_snsClient", _sns);
        _SetField(client, "_queueUrl", lane == MessageLane.Bus ? "https://sqs.local/bus-orders" : string.Empty);
        return client;
    }

    private static IOptions<AmazonSqsMessagingOptions> _Options(bool autoProvision)
    {
        return Options.Create(
            new AmazonSqsMessagingOptions
            {
                Region = Amazon.RegionEndpoint.USEast1,
                SqsServiceUrl = "http://localhost:4566",
                SnsServiceUrl = "http://localhost:4566",
                AutoProvision = autoProvision,
            }
        );
    }

    private static TransportMessage _Message(string name)
    {
        return new TransportMessage(
            new Dictionary<string, string?>(StringComparer.Ordinal)
            {
                [Headers.MessageId] = "message-1",
                [Headers.MessageName] = name,
            },
            "{}"u8.ToArray()
        );
    }

    private static void _SetField(object target, string name, object value)
    {
        target
            .GetType()
            .GetField(name, BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.DeclaredOnly)!
            .SetValue(target, value);
    }
}

/// <summary>SQS takes the visibility timeout and the receive wait as whole seconds within its own limits.</summary>
public sealed class AmazonSqsMessagingOptionsValidatorTests
{
    [Theory]
    [InlineData(0.0, 5.0)]
    [InlineData(43201.0, 5.0)]
    [InlineData(30.5, 5.0)]
    [InlineData(30.0, 21.0)]
    [InlineData(30.0, -1.0)]
    [InlineData(30.0, 2.5)]
    public void should_reject_values_sqs_cannot_take(double visibilitySeconds, double waitSeconds)
    {
        // given
        var options = new AmazonSqsMessagingOptions
        {
            Region = Amazon.RegionEndpoint.USEast1,
            VisibilityTimeout = TimeSpan.FromSeconds(visibilitySeconds),
            ReceiveWaitTime = TimeSpan.FromSeconds(waitSeconds),
        };

        // when
        var result = new AmazonSqsMessagingOptionsValidator().Validate(options);

        // then
        result.IsValid.Should().BeFalse();
    }

    [Theory]
    [InlineData(1.0, 0.0)]
    [InlineData(43200.0, 20.0)]
    public void should_accept_the_sqs_limits(double visibilitySeconds, double waitSeconds)
    {
        var options = new AmazonSqsMessagingOptions
        {
            Region = Amazon.RegionEndpoint.USEast1,
            VisibilityTimeout = TimeSpan.FromSeconds(visibilitySeconds),
            ReceiveWaitTime = TimeSpan.FromSeconds(waitSeconds),
        };

        new AmazonSqsMessagingOptionsValidator().Validate(options).IsValid.Should().BeTrue();
    }
}
