// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Collections.Concurrent;
using System.Reflection;
using Amazon.SimpleNotificationService;
using Amazon.SimpleNotificationService.Model;
using Amazon.SQS;
using Amazon.SQS.Model;
using Headless.Hosting;
using Headless.Messaging;
using Headless.Messaging.Aws;
using Headless.Testing.Tests;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NSubstitute.ExceptionExtensions;

namespace Tests;

/// <summary>
/// Publishers and registered consumers only look the shared topology up, so the runtime identity needs no create or
/// policy permissions and a missing object names <c>resources provision</c>; a runtime subscription creates its own
/// topology, with raw message delivery.
/// </summary>
public sealed class AwsTopologyLookupTests : TestBase
{
    private readonly IAmazonSQS _sqs = Substitute.For<IAmazonSQS>();
    private readonly IAmazonSimpleNotificationService _sns = Substitute.For<IAmazonSimpleNotificationService>();

    [Fact]
    public async Task should_look_up_a_queue_lane_queue_instead_of_creating_it()
    {
        // given
        _sqs.GetQueueUrlAsync("queue-orders", Arg.Any<CancellationToken>())
            .Returns(new GetQueueUrlResponse { QueueUrl = "https://sqs.local/queue-orders" });
        await using var client = _CreateConsumer(MessageLane.Queue, provisionsTopology: false);

        // when
        var queueUrls = await client.FetchMessageNamesAsync(["orders"], AbortToken);

        // then
        queueUrls.Should().Equal("https://sqs.local/queue-orders");
        await _sqs.DidNotReceiveWithAnyArgs().CreateQueueAsync(default(CreateQueueRequest)!, AbortToken);
    }

    [Fact]
    public async Task should_name_resources_provision_when_the_queue_does_not_exist()
    {
        // given
        _sqs.GetQueueUrlAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(
                new QueueDoesNotExistException("missing") { ErrorCode = "AWS.SimpleQueueService.NonExistentQueue" }
            );
        await using var client = _CreateConsumer(MessageLane.Queue, provisionsTopology: false);

        // when
        var act = async () => await client.FetchMessageNamesAsync(["orders"], AbortToken);

        // then: the failure the resources startup check reports, so readiness stays held until the deploy step runs
        await act.Should()
            .ThrowAsync<ResourceProvisioningException>()
            .WithMessage("*SQS queue 'queue-orders' does not exist*resources provision*");
        await _sqs.DidNotReceiveWithAnyArgs().CreateQueueAsync(default(CreateQueueRequest)!, AbortToken);
    }

    [Fact]
    public async Task should_name_the_lookup_permission_when_the_lookup_is_denied()
    {
        // given
        _sqs.GetQueueUrlAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new AmazonSQSException("denied") { ErrorCode = "AccessDenied" });
        await using var client = _CreateConsumer(MessageLane.Queue, provisionsTopology: false);

        // when
        var act = async () => await client.FetchMessageNamesAsync(["orders"], AbortToken);

        // then
        await act.Should()
            .ThrowAsync<InvalidOperationException>()
            .WithMessage("*AccessDenied*sqs:GetQueueUrl*The host creates no topology*resources provision*");
    }

    [Fact]
    public async Task should_leave_bus_topology_alone_for_a_registered_subscription()
    {
        // given
        await using var client = _CreateConsumer(MessageLane.Bus, provisionsTopology: false);

        // when
        var names = await client.FetchMessageNamesAsync(["orders.placed"], AbortToken);
        await client.SubscribeAsync(names, AbortToken);

        // then
        names.Should().Equal("orders.placed");
        _sns.ReceivedCalls().Should().BeEmpty();
        await _sqs.DidNotReceiveWithAnyArgs().SetQueueAttributesAsync(default!, default!, AbortToken);
        await _sqs.DidNotReceiveWithAnyArgs().CreateQueueAsync(default(CreateQueueRequest)!, AbortToken);
        await client.WaitUntilReadyAsync(AbortToken);
    }

    [Fact]
    public async Task should_let_a_runtime_subscription_create_its_topics_and_queue_policy()
    {
        // given
        _GivenBusQueue();
        await using var client = _CreateConsumer(MessageLane.Bus, provisionsTopology: true);

        // when
        await client.FetchMessageNamesAsync(["orders.placed"], AbortToken);

        // then: the topic and the policy that lets it deliver to the queue
        await _sns.Received(1).CreateTopicAsync(Arg.Any<CreateTopicRequest>(), Arg.Any<CancellationToken>());
        await _sqs.Received(1)
            .SetQueueAttributesAsync(
                "https://sqs.local/bus-orders",
                Arg.Is<Dictionary<string, string>>(a =>
                    a["Policy"].Contains("arn:aws:sns:::bus-orders-placed", StringComparison.Ordinal)
                ),
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task should_subscribe_bus_queues_with_raw_message_delivery()
    {
        // given
        _GivenBusQueue();
        _sns.SubscribeAsync(Arg.Any<SubscribeRequest>(), Arg.Any<CancellationToken>())
            .Returns(new SubscribeResponse { SubscriptionArn = "arn:aws:sns:::bus-orders-placed:sub" });
        await using var client = _CreateConsumer(MessageLane.Bus, provisionsTopology: true);

        // when
        await client.SubscribeAsync(["orders.placed"], AbortToken);

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
        await using var client = _CreateConsumer(MessageLane.Bus, provisionsTopology: true);

        // when
        await client.SubscribeAsync(["orders.placed"], AbortToken);

        // then
        await _sns.Received(1)
            .SetSubscriptionAttributesAsync(
                "arn:aws:sns:::bus-orders-placed:old",
                "RawMessageDelivery",
                "true",
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task should_create_a_topic_once_for_every_client_of_the_process()
    {
        // given: two listening clients of one runtime subscription share the process's services
        _GivenBusQueue();
        _sns.SubscribeAsync(Arg.Any<SubscribeRequest>(), Arg.Any<CancellationToken>())
            .Returns(new SubscribeResponse { SubscriptionArn = "arn:aws:sns:::bus-orders-placed:sub" });
        var services = new ServiceCollection().AddSingleton<SnsTopicArnCache>().BuildServiceProvider();
        await using var first = _CreateConsumer(MessageLane.Bus, provisionsTopology: true, services);
        await using var second = _CreateConsumer(MessageLane.Bus, provisionsTopology: true, services);

        // when: each runs the topology step and subscribes
        foreach (var client in new[] { first, second })
        {
            var names = await client.FetchMessageNamesAsync(["orders.placed.fifo"], AbortToken);
            await client.SubscribeAsync(names, AbortToken);
        }

        // then: one create, still carrying the FIFO attributes, and both clients subscribe the resolved topic
        await _sns.Received(1)
            .CreateTopicAsync(
                Arg.Is<CreateTopicRequest>(r =>
                    r.Name.EndsWith(".fifo", StringComparison.Ordinal)
                    && r.Attributes["FifoTopic"] == "true"
                    && r.Attributes["ContentBasedDeduplication"] == "true"
                ),
                Arg.Any<CancellationToken>()
            );
        _sns.ReceivedCalls()
            .Count(call =>
                string.Equals(call.GetMethodInfo().Name, nameof(_sns.CreateTopicAsync), StringComparison.Ordinal)
            )
            .Should()
            .Be(1);
        await _sns.Received(2)
            .SubscribeAsync(
                Arg.Is<SubscribeRequest>(r => r.TopicArn == "arn:aws:sns:::bus-orders-placed"),
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task should_create_a_topic_again_after_its_creation_failed()
    {
        // given: the first create is throttled, the next one succeeds
        _GivenBusQueue();
        _sns.SubscribeAsync(Arg.Any<SubscribeRequest>(), Arg.Any<CancellationToken>())
            .Returns(new SubscribeResponse { SubscriptionArn = "arn:aws:sns:::bus-orders-placed:sub" });
        _sns.CreateTopicAsync(Arg.Any<CreateTopicRequest>(), Arg.Any<CancellationToken>())
            .Returns(
                Task.FromException<CreateTopicResponse>(
                    new AmazonSimpleNotificationServiceException("Rate exceeded") { ErrorCode = "Throttling" }
                ),
                Task.FromResult(new CreateTopicResponse { TopicArn = "arn:aws:sns:::bus-orders-placed" })
            );
        var services = new ServiceCollection().AddSingleton<SnsTopicArnCache>().BuildServiceProvider();
        await using var first = _CreateConsumer(MessageLane.Bus, provisionsTopology: true, services);
        await using var second = _CreateConsumer(MessageLane.Bus, provisionsTopology: true, services);

        // when
        var failed = async () => await first.SubscribeAsync(["orders.placed"], AbortToken);
        await failed.Should().ThrowAsync<InvalidOperationException>().WithMessage("*Throttling*");
        await second.SubscribeAsync(["orders.placed"], AbortToken);

        // then: the failure was not cached, so the second client created the topic and subscribed it
        await _sns.Received(2).CreateTopicAsync(Arg.Any<CreateTopicRequest>(), Arg.Any<CancellationToken>());
        await _sns.Received(1)
            .SubscribeAsync(
                Arg.Is<SubscribeRequest>(r => r.TopicArn == "arn:aws:sns:::bus-orders-placed"),
                Arg.Any<CancellationToken>()
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
            _Options()
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
    public async Task should_not_look_up_a_missing_topic_again_when_sends_retry_soon_after()
    {
        // given: the topic is not provisioned yet, so the lookup finds nothing
        _sns.FindTopicAsync("bus-orders").Returns((Topic?)null);
        await using var transport = new AmazonSnsBusTransport(
            Substitute.For<ILogger<AmazonSnsBusTransport>>(),
            _Options()
        );
        _SetField(transport, "_snsClient", _sns);
        _SetField(transport, "_topicArnMaps", new ConcurrentDictionary<string, string>(StringComparer.Ordinal));

        // when
        var first = await transport.SendAsync(_Message("orders"), AbortToken);
        var second = await transport.SendAsync(_Message("orders"), AbortToken);

        // then: both sends fail, and only the first one pays for the account-wide lookup
        first.Succeeded.Should().BeFalse();
        second.Succeeded.Should().BeFalse();
        await _sns.Received(1).FindTopicAsync("bus-orders");
    }

    [Fact]
    public async Task should_look_up_a_queue_destination_instead_of_creating_it()
    {
        // given
        _sqs.GetQueueUrlAsync("queue-orders", Arg.Any<CancellationToken>())
            .Returns(new GetQueueUrlResponse { QueueUrl = "https://sqs.local/queue-orders" });
        await using var transport = new AmazonSqsQueueTransport(
            Substitute.For<ILogger<AmazonSqsQueueTransport>>(),
            _Options()
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

    [Fact]
    public async Task should_fail_a_send_to_a_missing_queue_and_find_it_once_provisioned()
    {
        // given: the queue does not exist on the first send and does on the second
        _sqs.GetQueueUrlAsync("queue-orders", Arg.Any<CancellationToken>())
            .Returns(
                _ => Task.FromException<GetQueueUrlResponse>(new QueueDoesNotExistException("missing")),
                _ => Task.FromResult(new GetQueueUrlResponse { QueueUrl = "https://sqs.local/queue-orders" })
            );
        await using var transport = new AmazonSqsQueueTransport(
            Substitute.For<ILogger<AmazonSqsQueueTransport>>(),
            _Options()
        );
        _SetField(transport, "_sqsClient", _sqs);

        // when
        var first = await transport.SendAsync(_Message("orders"), AbortToken);
        var second = await transport.SendAsync(_Message("orders"), AbortToken);

        // then: the miss names the deploy step and is not cached
        first.Succeeded.Should().BeFalse();
        first.Exception!.ToString().Should().Contain("resources provision");
        second.Succeeded.Should().BeTrue();
        await _sqs.DidNotReceiveWithAnyArgs().CreateQueueAsync(default(CreateQueueRequest)!, AbortToken);
    }

    private void _GivenBusQueue()
    {
        _sns.CreateTopicAsync(Arg.Any<CreateTopicRequest>(), Arg.Any<CancellationToken>())
            .Returns(new CreateTopicResponse { TopicArn = "arn:aws:sns:::bus-orders-placed" });
        _sqs.GetQueueAttributesAsync(
                Arg.Is<GetQueueAttributesRequest>(r => r.QueueUrl == "https://sqs.local/bus-orders"),
                Arg.Any<CancellationToken>()
            )
            .Returns(
                new GetQueueAttributesResponse
                {
                    Attributes = new Dictionary<string, string>(StringComparer.Ordinal)
                    {
                        ["QueueArn"] = "arn:aws:sqs:::bus-orders",
                    },
                }
            );
    }

    private AmazonSqsConsumerClient _CreateConsumer(
        MessageLane lane,
        bool provisionsTopology,
        IServiceProvider? serviceProvider = null
    )
    {
        var client = new AmazonSqsConsumerClient(
            "orders",
            1,
            _Options(),
            Substitute.For<ILogger<AmazonSqsConsumerClient>>(),
            lane,
            serviceProvider: serviceProvider,
            provisionsTopology: provisionsTopology
        );
        _SetField(client, "_sqsClient", _sqs);
        _SetField(client, "_snsClient", _sns);
        _SetField(client, "_queueUrl", lane == MessageLane.Bus ? "https://sqs.local/bus-orders" : string.Empty);
        return client;
    }

    private static IOptions<AmazonSqsMessagingOptions> _Options()
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
