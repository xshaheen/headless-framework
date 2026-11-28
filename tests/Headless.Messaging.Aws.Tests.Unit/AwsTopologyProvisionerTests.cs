// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Amazon.Auth.AccessControlPolicy;
using Amazon.SimpleNotificationService;
using Amazon.SimpleNotificationService.Model;
using Amazon.SQS;
using Amazon.SQS.Model;
using Headless.Hosting;
using Headless.Messaging;
using Headless.Messaging.Aws;
using Headless.Messaging.Transport;
using Headless.Testing.Tests;
using Microsoft.Extensions.Options;
using NSubstitute.ExceptionExtensions;

namespace Tests;

public sealed class AwsTopologyProvisionerTests : TestBase
{
    private const string _TopicArn = "arn:aws:sns:us-east-1:1:bus-placed";
    private const string _BusQueueUrl = "https://sqs.local/1/bus-billing";
    private const string _BusQueueArn = "arn:aws:sqs:us-east-1:1:bus-billing";
    private const string _OrdersQueueUrl = "https://sqs.local/1/queue-orders";
    private const string _InvoicesQueueUrl = "https://sqs.local/1/queue-invoices";
    private const string _SubscriptionArn = _TopicArn + ":sub";

    private readonly IAmazonSQS _sqs = Substitute.For<IAmazonSQS>();
    private readonly IAmazonSimpleNotificationService _sns = Substitute.For<IAmazonSimpleNotificationService>();

    // The host consumes "placed" on the Bus as "billing", consumes the "orders" queue, and only sends to
    // "invoices".
    private static readonly MessagingTopology _Topology = new(
        [
            new TopologySubscription("billing", MessageLane.Bus, ConsumerSubscriptionKind.Competing, ["placed"]),
            new TopologySubscription("orders", MessageLane.Queue, ConsumerSubscriptionKind.Competing, ["orders"]),
        ],
        [new TopologyMessage("invoices", MessageLane.Queue)]
    );

    [Fact]
    public void should_report_the_public_kind_constant()
    {
        // given
        var provisioner = new AwsTopologyProvisioner(Options.Create(_Options()));

        // when
        var kind = provisioner.Kind;

        // then
        // The value is what an operator passes to 'resources provision -k', so the constant must not change it.
        kind.Should().Be(SetupMessagingAws.TopologyResourceKind).And.Be("AmazonSqs");
    }

    [Fact]
    public void should_own_only_the_queues_this_host_consumes()
    {
        // when
        var plan = AwsTopologyProvisioner.Plan(_Topology);

        // then
        plan.Topics.Should().Equal("bus-placed");
        plan.Queues.Should().HaveCount(3);
        var bus = plan.Queues.Should().ContainSingle(q => q.Name == "bus-billing").Which;
        bus.Owned.Should().BeTrue();
        bus.Topics.Should().Equal("bus-placed");
        plan.Queues.Should().ContainSingle(q => q.Name == "queue-orders").Which.Owned.Should().BeTrue();
        plan.Queues.Should().ContainSingle(q => q.Name == "queue-invoices").Which.Owned.Should().BeFalse();
    }

    [Fact]
    public void should_skip_every_instance_subscriptions_and_add_a_topic_per_published_bus_message()
    {
        // given
        var topology = new MessagingTopology(
            [new TopologySubscription("cache", MessageLane.Bus, ConsumerSubscriptionKind.EveryInstance, ["paid"])],
            [new TopologyMessage("shipped", MessageLane.Bus)]
        );

        // when
        var plan = AwsTopologyProvisioner.Plan(topology);

        // then: a per-process queue is the host's own; a published Bus message needs only its topic
        plan.Queues.Should().BeEmpty();
        plan.Topics.Should().Equal("bus-shipped");
    }

    [Fact]
    public void should_apply_the_host_prefix_to_every_name()
    {
        // when
        var plan = AwsTopologyProvisioner.Plan(_Topology, "dev");

        // then
        plan.Topics.Should().OnlyContain(t => t.Contains("dev", StringComparison.Ordinal));
        plan.Queues.Should().OnlyContain(q => q.Name.Contains("dev", StringComparison.Ordinal));
    }

    [Fact]
    public async Task should_provision_topics_queues_policy_and_raw_subscriptions()
    {
        // given
        _GivenCreates();
        _GivenBusQueueAttributes(policy: null);

        // when
        await _Provisioner().ProvisionAsync(_Topology, AbortToken);

        // then
        await _sns.Received(1)
            .CreateTopicAsync(Arg.Is<CreateTopicRequest>(r => r.Name == "bus-placed"), Arg.Any<CancellationToken>());
        foreach (var name in new[] { "bus-billing", "queue-orders", "queue-invoices" })
        {
            await _sqs.Received(1)
                .CreateQueueAsync(Arg.Is<CreateQueueRequest>(r => r.QueueName == name), Arg.Any<CancellationToken>());
        }

        await _sqs.Received(1)
            .SetQueueAttributesAsync(
                _BusQueueUrl,
                Arg.Is<Dictionary<string, string>>(a => _Allows(a["Policy"])),
                Arg.Any<CancellationToken>()
            );
        await _sns.Received(1)
            .SubscribeAsync(
                Arg.Is<SubscribeRequest>(r =>
                    r.TopicArn == _TopicArn
                    && r.Endpoint == _BusQueueArn
                    && r.Protocol == "sqs"
                    && r.Attributes["RawMessageDelivery"] == "true"
                ),
                Arg.Any<CancellationToken>()
            );
        await _sns.Received(1)
            .SetSubscriptionAttributesAsync(
                _SubscriptionArn,
                "RawMessageDelivery",
                "true",
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task should_leave_an_already_allowing_queue_policy_unchanged_on_a_second_provision()
    {
        // given
        _GivenCreates();
        _GivenBusQueueAttributes(_AllowingPolicy());

        // when
        await _Provisioner().ProvisionAsync(_Topology, AbortToken);

        // then: CreateTopic, CreateQueue, and Subscribe are idempotent; the policy is written only when it lacks the topic
        await _sqs.DidNotReceiveWithAnyArgs().SetQueueAttributesAsync(default!, default!, AbortToken);
        await _sns.Received(1).SubscribeAsync(Arg.Any<SubscribeRequest>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task should_pass_verify_on_a_provisioned_topology_without_writing()
    {
        // given
        _GivenExisting();

        // when
        await _Provisioner().VerifyAsync(_Topology, AbortToken);

        // then
        _AssertNoWrites();
    }

    [Fact]
    public async Task should_name_every_missing_object_when_verify_finds_nothing()
    {
        // given: an account with no topics and no queues
        _sns.ListTopicsAsync(Arg.Any<ListTopicsRequest>(), Arg.Any<CancellationToken>())
            .Returns(new ListTopicsResponse { Topics = null });
        _sqs.GetQueueUrlAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new QueueDoesNotExistException("missing"));

        // when
        var act = () => _Provisioner().VerifyAsync(_Topology, AbortToken);

        // then
        var failure = await act.Should().ThrowAsync<ResourceProvisioningException>();
        failure
            .Which.Message.Should()
            .Contain("SNS topic 'bus-placed'")
            .And.Contain("SQS queue 'bus-billing'")
            .And.Contain("SQS queue 'queue-orders'")
            .And.Contain("SQS queue 'queue-invoices'")
            .And.Contain("resources provision");
        _AssertNoWrites();
    }

    [Fact]
    public async Task should_fail_verify_when_the_queue_policy_or_the_raw_subscription_is_missing()
    {
        // given: the queues and the topic exist, but the policy and the subscription were never set up
        _GivenExisting();
        _GivenBusQueueAttributes(policy: null);
        _sns.ListSubscriptionsByTopicAsync(Arg.Any<ListSubscriptionsByTopicRequest>(), Arg.Any<CancellationToken>())
            .Returns(new ListSubscriptionsByTopicResponse { Subscriptions = null });

        // when
        var act = () => _Provisioner().VerifyAsync(_Topology, AbortToken);

        // then
        var failure = await act.Should().ThrowAsync<ResourceProvisioningException>();
        failure
            .Which.Message.Should()
            .Contain("queue policy of 'bus-billing'")
            .And.Contain("SNS subscription of SQS queue 'bus-billing'");
        _AssertNoWrites();
    }

    [Fact]
    public async Task should_fail_verify_when_the_subscription_wraps_messages()
    {
        // given
        _GivenExisting();
        _sns.GetSubscriptionAttributesAsync(_SubscriptionArn, Arg.Any<CancellationToken>())
            .Returns(
                new GetSubscriptionAttributesResponse
                {
                    Attributes = new Dictionary<string, string>(StringComparer.Ordinal)
                    {
                        ["RawMessageDelivery"] = "false",
                    },
                }
            );

        // when
        var act = () => _Provisioner().VerifyAsync(_Topology, AbortToken);

        // then: a wrapped message would be deleted as malformed, so the check fails before the host serves
        await act.Should().ThrowAsync<ResourceProvisioningException>().WithMessage("*raw message delivery*");
    }

    [Fact]
    public async Task should_purge_only_the_queues_this_host_consumes()
    {
        // given: a purge of one queue already runs, which empties it all the same
        _GivenExisting();
        _sqs.PurgeQueueAsync(_OrdersQueueUrl, Arg.Any<CancellationToken>())
            .ThrowsAsync(new PurgeQueueInProgressException("in progress"));

        // when
        await _Provisioner().PurgeAsync(_Topology, AbortToken);

        // then
        await _sqs.Received(1).PurgeQueueAsync(_BusQueueUrl, Arg.Any<CancellationToken>());
        await _sqs.Received(1).PurgeQueueAsync(_OrdersQueueUrl, Arg.Any<CancellationToken>());
        await _sqs.DidNotReceive().PurgeQueueAsync(_InvoicesQueueUrl, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task should_delete_owned_queues_and_their_subscriptions_and_keep_the_topics()
    {
        // given
        _GivenExisting();

        // when
        await _Provisioner().DeleteAsync(_Topology, AbortToken);

        // then
        await _sns.Received(1).UnsubscribeAsync(_SubscriptionArn, Arg.Any<CancellationToken>());
        await _sqs.Received(1).DeleteQueueAsync(_BusQueueUrl, Arg.Any<CancellationToken>());
        await _sqs.Received(1).DeleteQueueAsync(_OrdersQueueUrl, Arg.Any<CancellationToken>());
        await _sqs.DidNotReceive().DeleteQueueAsync(_InvoicesQueueUrl, Arg.Any<CancellationToken>());
        await _sns.DidNotReceiveWithAnyArgs().DeleteTopicAsync(default(string)!, AbortToken);
        await _sns.DidNotReceiveWithAnyArgs().DeleteTopicAsync(default(DeleteTopicRequest)!, AbortToken);
    }

    [Fact]
    public async Task should_skip_a_queue_that_is_already_gone_on_delete()
    {
        // given
        _GivenExisting();
        _sqs.GetQueueUrlAsync("queue-orders", Arg.Any<CancellationToken>())
            .ThrowsAsync(new QueueDoesNotExistException("missing"));

        // when
        await _Provisioner().DeleteAsync(_Topology, AbortToken);

        // then
        await _sqs.Received(1).DeleteQueueAsync(_BusQueueUrl, Arg.Any<CancellationToken>());
        await _sqs.DidNotReceive().DeleteQueueAsync(_OrdersQueueUrl, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task should_report_each_object_and_a_missing_one_without_throwing()
    {
        // given
        _GivenExisting();
        _sqs.GetQueueUrlAsync("queue-invoices", Arg.Any<CancellationToken>())
            .ThrowsAsync(new QueueDoesNotExistException("missing"));
        _sqs.GetQueueAttributesAsync(
                Arg.Is<GetQueueAttributesRequest>(r => r.AttributeNames.Contains("ApproximateNumberOfMessages")),
                Arg.Any<CancellationToken>()
            )
            .Returns(
                new GetQueueAttributesResponse
                {
                    Attributes = new Dictionary<string, string>(StringComparer.Ordinal)
                    {
                        ["ApproximateNumberOfMessages"] = "3",
                        ["ApproximateNumberOfMessagesNotVisible"] = "1",
                    },
                }
            );

        // when
        var status = await _Provisioner().GetStatusAsync(_Topology, AbortToken);

        // then
        status
            .Should()
            .BeEquivalentTo(
                [
                    new KeyValuePair<string, string>("bus-placed", "topic"),
                    new KeyValuePair<string, string>("bus-billing", "3 messages, 1 in flight"),
                    new KeyValuePair<string, string>("queue-invoices", "queue missing"),
                    new KeyValuePair<string, string>("queue-orders", "3 messages, 1 in flight"),
                ],
                o => o.WithStrictOrdering()
            );
    }

    private AwsTopologyProvisioner _Provisioner() => AwsTopologyProvisioner.Create(_Options(), _sqs, _sns);

    private static AmazonSqsMessagingOptions _Options() =>
        new()
        {
            Region = Amazon.RegionEndpoint.USEast1,
            SqsServiceUrl = "http://localhost:4566",
            SnsServiceUrl = "http://localhost:4566",
        };

    private void _GivenCreates()
    {
        _sns.CreateTopicAsync(Arg.Any<CreateTopicRequest>(), Arg.Any<CancellationToken>())
            .Returns(new CreateTopicResponse { TopicArn = _TopicArn });
        _sqs.CreateQueueAsync(Arg.Any<CreateQueueRequest>(), Arg.Any<CancellationToken>())
            .Returns(call => new CreateQueueResponse { QueueUrl = _Url(call.Arg<CreateQueueRequest>().QueueName) });
        _sns.SubscribeAsync(Arg.Any<SubscribeRequest>(), Arg.Any<CancellationToken>())
            .Returns(new SubscribeResponse { SubscriptionArn = _SubscriptionArn });
    }

    // A fully provisioned topology: the topic, the three queues, a policy allowing the topic, and a raw subscription.
    private void _GivenExisting()
    {
        _sns.ListTopicsAsync(Arg.Any<ListTopicsRequest>(), Arg.Any<CancellationToken>())
            .Returns(new ListTopicsResponse { Topics = [new Topic { TopicArn = _TopicArn }] });
        _sqs.GetQueueUrlAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(call => new GetQueueUrlResponse { QueueUrl = _Url(call.Arg<string>()) });
        _GivenBusQueueAttributes(_AllowingPolicy());
        _sns.ListSubscriptionsByTopicAsync(
                Arg.Is<ListSubscriptionsByTopicRequest>(r => r.TopicArn == _TopicArn),
                Arg.Any<CancellationToken>()
            )
            .Returns(
                new ListSubscriptionsByTopicResponse
                {
                    Subscriptions =
                    [
                        new Subscription
                        {
                            Protocol = "sqs",
                            Endpoint = _BusQueueArn,
                            SubscriptionArn = _SubscriptionArn,
                        },
                        // Another service's queue on the same topic: never touched.
                        new Subscription
                        {
                            Protocol = "sqs",
                            Endpoint = "arn:aws:sqs:us-east-1:1:bus-other",
                            SubscriptionArn = _TopicArn + ":other",
                        },
                    ],
                }
            );
        _sns.GetSubscriptionAttributesAsync(_SubscriptionArn, Arg.Any<CancellationToken>())
            .Returns(
                new GetSubscriptionAttributesResponse
                {
                    Attributes = new Dictionary<string, string>(StringComparer.Ordinal)
                    {
                        ["RawMessageDelivery"] = "true",
                    },
                }
            );
    }

    private void _GivenBusQueueAttributes(string? policy)
    {
        var attributes = new Dictionary<string, string>(StringComparer.Ordinal) { ["QueueArn"] = _BusQueueArn };

        if (policy is not null)
        {
            attributes["Policy"] = policy;
        }

        _sqs.GetQueueAttributesAsync(
                Arg.Is<GetQueueAttributesRequest>(r =>
                    r.QueueUrl == _BusQueueUrl && r.AttributeNames.Contains("Policy")
                ),
                Arg.Any<CancellationToken>()
            )
            .Returns(new GetQueueAttributesResponse { Attributes = attributes });
    }

    private static string _AllowingPolicy()
    {
        var policy = new Policy();
        policy.AddSqsPermissions([_TopicArn], _BusQueueArn);
        return policy.ToJson();
    }

    private static bool _Allows(string json) => Policy.FromJson(json).HasSqsPermission(_TopicArn, _BusQueueArn);

    private static string _Url(string queueName) => "https://sqs.local/1/" + queueName;

    private void _AssertNoWrites()
    {
        string[] writes =
        [
            "CreateTopicAsync",
            "CreateQueueAsync",
            "SetQueueAttributesAsync",
            "SubscribeAsync",
            "SetSubscriptionAttributesAsync",
            "UnsubscribeAsync",
            "DeleteQueueAsync",
            "DeleteTopicAsync",
            "PurgeQueueAsync",
        ];

        _sqs.ReceivedCalls()
            .Concat(_sns.ReceivedCalls())
            .Select(static call => call.GetMethodInfo().Name)
            .Should()
            .NotContain(writes);
    }
}
