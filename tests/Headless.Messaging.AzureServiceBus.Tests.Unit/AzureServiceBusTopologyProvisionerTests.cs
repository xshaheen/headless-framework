// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Azure.Messaging.ServiceBus.Administration;
using Headless.Hosting;
using Headless.Messaging;
using Headless.Messaging.AzureServiceBus;
using Headless.Messaging.Transport;
using Headless.Testing.Tests;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Tests;

/// <summary>
/// The Azure Service Bus topology deploy step against an in-memory namespace: what provision creates, what verify reads
/// and reports, what purge and delete touch, and the consumer identities it leaves to the running host.
/// </summary>
public sealed class AzureServiceBusTopologyProvisionerTests : TestBase
{
    private const string _TopicPath = "headless";
    private const string _Identity = "billing.invoices";
    private const string _OrderPlaced = "orders.placed";
    private const string _OrderShipped = "orders.shipped";
    private const string _InvoiceQueue = "billing.invoice-requested";
    private const string _ReceiptQueue = "billing.receipt-sent";

    private static readonly string _Subscription = AzureServiceBusConsumerClientFactory.BusSubscriptionName(_Identity);
    private static readonly string _SubscriptionKey = FakeServiceBusNamespace.Key(_TopicPath, _Subscription);

    private readonly FakeServiceBusNamespace _namespace = new();

    // One competing Bus consumer of two messages, one every-instance consumer, one consumed queue, and one queue this
    // host only publishes to.
    private static readonly MessagingTopology _Topology = new(
        [
            new TopologySubscription(
                _Identity,
                MessageLane.Bus,
                ConsumerSubscriptionKind.Competing,
                [_OrderPlaced, _OrderShipped]
            ),
            new TopologySubscription(
                "pricing.price-cache",
                MessageLane.Bus,
                ConsumerSubscriptionKind.EveryInstance,
                [_OrderPlaced]
            ),
            new TopologySubscription(
                _InvoiceQueue,
                MessageLane.Queue,
                ConsumerSubscriptionKind.Competing,
                [_InvoiceQueue]
            ),
        ],
        [
            new TopologyMessage(_OrderPlaced, MessageLane.Bus),
            new TopologyMessage(_InvoiceQueue, MessageLane.Queue),
            new TopologyMessage(_ReceiptQueue, MessageLane.Queue),
        ]
    );

    [Fact]
    public void should_expose_the_azure_service_bus_kind()
    {
        // given
        ITransportTopologyProvisioner provisioner = _CreateProvisioner();

        // then
        SetupMessagingAzureServiceBus.TopologyResourceKind.Should().Be("AzureServiceBus");
        provisioner.Kind.Should().Be(SetupMessagingAzureServiceBus.TopologyResourceKind);
        provisioner.CanPurge.Should().BeTrue();
        provisioner.CanDelete.Should().BeTrue();
        provisioner.StartupCheck.Should().Be(ResourceStartupCheck.GateReadiness);
    }

    [Fact]
    public async Task should_register_the_provisioner_with_the_transport()
    {
        // given
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddHeadlessMessaging(setup =>
            setup.UseAzureServiceBus(
                "Endpoint=sb://mynamespace.servicebus.windows.net/;SharedAccessKeyName=myPolicy;SharedAccessKey=myKey"
            )
        );
        await using var provider = services.BuildServiceProvider();

        // when
        var provisioners = provider.GetServices<ITransportTopologyProvisioner>().ToList();

        // then
        provisioners
            .Should()
            .ContainSingle()
            .Which.Kind.Should()
            .Be(SetupMessagingAzureServiceBus.TopologyResourceKind);
    }

    [Fact]
    public void should_plan_shared_entities_only_and_own_only_the_queues_this_host_consumes()
    {
        // when
        var plan = AzureServiceBusTopologyProvisioner.Plan(_Topology, _Options());

        // then
        plan.Topics.Should().Equal((_TopicPath, true));
        plan.Subscriptions.Should()
            .ContainSingle()
            .Which.Should()
            .BeEquivalentTo(
                new AzureServiceBusTopologySubscription(_TopicPath, _Subscription, [_OrderPlaced, _OrderShipped])
            );
        plan.Queues.Select(static q => (q.Name, q.Owned)).Should().Equal((_InvoiceQueue, true), (_ReceiptQueue, false));
    }

    [Fact]
    public async Task should_create_the_topic_subscription_with_one_rule_per_message_and_every_queue()
    {
        // when
        await _CreateProvisioner().ProvisionAsync(_Topology, AbortToken);

        // then
        _namespace.Topics.Should().BeEquivalentTo([_TopicPath]);
        _namespace.Subscriptions.Keys.Should().BeEquivalentTo([_SubscriptionKey]);
        _namespace.Subscriptions[_SubscriptionKey].Keys.Should().BeEquivalentTo([_OrderPlaced, _OrderShipped]);
        _namespace
            .Subscriptions[_SubscriptionKey][_OrderPlaced]
            .Should()
            .BeOfType<CorrelationRuleFilter>()
            .Which.Subject.Should()
            .Be(_OrderPlaced);
        _namespace.Queues.Should().BeEquivalentTo([_InvoiceQueue, _ReceiptQueue]);
    }

    [Fact]
    public async Task should_write_nothing_when_provisioning_a_topology_that_exists()
    {
        // given
        var provisioner = _CreateProvisioner();
        await provisioner.ProvisionAsync(_Topology, AbortToken);
        _namespace.Writes.Clear();

        // when
        await provisioner.ProvisionAsync(_Topology, AbortToken);

        // then
        _namespace.Writes.Should().BeEmpty();
    }

    [Fact]
    public async Task should_reconcile_a_subscription_to_the_messages_its_consumer_handles()
    {
        // given a subscription created by hand, still holding the match-all rule and a message nobody consumes now
        _namespace.Topics.Add(_TopicPath);
        _namespace.AddSubscription(
            _TopicPath,
            _Subscription,
            RuleProperties.DefaultRuleName,
            "orders.cancelled",
            _OrderPlaced
        );

        // when
        await _CreateProvisioner().ProvisionAsync(_Topology, AbortToken);

        // then
        _namespace.Subscriptions[_SubscriptionKey].Keys.Should().BeEquivalentTo([_OrderPlaced, _OrderShipped]);
        _namespace.Subscriptions[_SubscriptionKey][_OrderPlaced].Should().BeOfType<CorrelationRuleFilter>();
    }

    [Fact]
    public async Task should_create_no_subscription_for_an_every_instance_consumer()
    {
        // given
        var topology = new MessagingTopology(
            [
                new TopologySubscription(
                    "pricing.price-cache",
                    MessageLane.Bus,
                    ConsumerSubscriptionKind.EveryInstance,
                    [_OrderPlaced]
                ),
            ],
            []
        );

        // when
        await _CreateProvisioner().ProvisionAsync(topology, AbortToken);

        // then
        _namespace.Topics.Should().BeEquivalentTo([_TopicPath]);
        _namespace.Subscriptions.Should().BeEmpty();
    }

    [Fact]
    public async Task should_subscribe_on_a_custom_producer_topic_without_managing_its_rules()
    {
        // given
        var options = _Options();
        options.CustomProducers.Add(
            new ServiceBusProducerDescriptor("Shipment", "shipments", createSubscription: true, enableSessions: false)
        );
        options.CustomProducers.Add(
            new ServiceBusProducerDescriptor("Audit", "audits", createSubscription: false, enableSessions: false)
        );

        // when
        await _CreateProvisioner(options).ProvisionAsync(_Topology, AbortToken);

        // then
        _namespace.Topics.Should().BeEquivalentTo(["audits", _TopicPath, "shipments"]);
        _namespace
            .Subscriptions.Keys.Should()
            .BeEquivalentTo([_SubscriptionKey, FakeServiceBusNamespace.Key("shipments", _Subscription)]);
        _namespace
            .Subscriptions[FakeServiceBusNamespace.Key("shipments", _Subscription)]
            .Keys.Should()
            .BeEquivalentTo([RuleProperties.DefaultRuleName]);
    }

    [Fact]
    public async Task should_report_every_missing_entity_and_the_fix_without_writing()
    {
        // when
        var act = () => _CreateProvisioner().VerifyAsync(_Topology, AbortToken);

        // then
        var error = await act.Should().ThrowAsync<ResourceProvisioningException>();
        error
            .Which.Message.Should()
            .Contain($"topic '{_TopicPath}' does not exist")
            .And.Contain($"queue '{_InvoiceQueue}' does not exist")
            .And.Contain($"queue '{_ReceiptQueue}' does not exist")
            .And.Contain("resources provision");
        _namespace.Writes.Should().BeEmpty();
    }

    [Fact]
    public async Task should_pass_verify_after_provision_in_one_read_per_listing_and_per_subscription()
    {
        // given
        var provisioner = _CreateProvisioner();
        await provisioner.ProvisionAsync(_Topology, AbortToken);
        _namespace.Administration.ClearReceivedCalls();

        // when
        await provisioner.VerifyAsync(_Topology, AbortToken);

        // then the topic listing, the queue listing, and the one shared subscription's rules
        _namespace.Administration.ReceivedCalls().Should().HaveCount(3);
        _ = _namespace.Administration.Received(1).GetTopicsAsync(AbortToken);
        _ = _namespace.Administration.Received(1).GetQueuesAsync(AbortToken);
        _ = _namespace.Administration.Received(1).GetRulesAsync(_TopicPath, _Subscription, AbortToken);
    }

    [Fact]
    public async Task should_fail_verify_when_a_subscription_rules_no_longer_match_its_consumer()
    {
        // given a subscription provisioned before the consumer stopped handling one message and started another
        var provisioner = _CreateProvisioner();
        await provisioner.ProvisionAsync(_Topology, AbortToken);
        _namespace.Subscriptions[_SubscriptionKey].Remove(_OrderShipped);
        _namespace.Subscriptions[_SubscriptionKey]["orders.cancelled"] = new TrueRuleFilter();

        // when
        var act = () => provisioner.VerifyAsync(_Topology, AbortToken);

        // then
        var error = await act.Should().ThrowAsync<ResourceProvisioningException>();
        error
            .Which.Message.Should()
            .Contain($"lacks the rules '{_OrderShipped}'")
            .And.Contain("holds the rules 'orders.cancelled'");
    }

    [Fact]
    public async Task should_fail_verify_when_the_subscription_is_missing()
    {
        // given
        var provisioner = _CreateProvisioner();
        await provisioner.ProvisionAsync(_Topology, AbortToken);
        _namespace.Subscriptions.Remove(_SubscriptionKey);

        // when
        var act = () => provisioner.VerifyAsync(_Topology, AbortToken);

        // then
        var error = await act.Should().ThrowAsync<ResourceProvisioningException>();
        error.Which.Message.Should().Contain($"subscription '{_Subscription}' on topic '{_TopicPath}' does not exist");
    }

    [Fact]
    public async Task should_purge_owned_entities_and_their_dead_letters_only()
    {
        // given
        var provisioner = _CreateProvisioner();
        await provisioner.ProvisionAsync(_Topology, AbortToken);
        _namespace.Messages[_SubscriptionKey] = 600;
        _namespace.Messages[FakeServiceBusNamespace.DeadLetter(_SubscriptionKey)] = 3;
        _namespace.Messages[_InvoiceQueue] = 2;
        _namespace.Messages[FakeServiceBusNamespace.DeadLetter(_InvoiceQueue)] = 1;
        _namespace.Messages[_ReceiptQueue] = 4;
        _namespace.Writes.Clear();

        // when
        await provisioner.PurgeAsync(_Topology, AbortToken);

        // then
        _namespace
            .Messages.Where(static m => m.Value > 0)
            .Should()
            .BeEquivalentTo([new KeyValuePair<string, int>(_ReceiptQueue, 4)]);
        _namespace.Writes.Should().BeEmpty();
    }

    [Fact]
    public async Task should_fail_purge_naming_an_owned_entity_that_does_not_exist()
    {
        // when
        var act = () => _CreateProvisioner().PurgeAsync(_Topology, AbortToken);

        // then
        var error = await act.Should().ThrowAsync<ResourceProvisioningException>();
        error
            .Which.Message.Should()
            .Contain($"subscription '{_Subscription}' on topic '{_TopicPath}' does not exist")
            .And.Contain($"queue '{_InvoiceQueue}' does not exist")
            .And.NotContain(_ReceiptQueue);
    }

    [Fact]
    public async Task should_delete_owned_subscriptions_and_queues_and_keep_the_shared_topic()
    {
        // given
        var provisioner = _CreateProvisioner();
        await provisioner.ProvisionAsync(_Topology, AbortToken);

        // when
        await provisioner.DeleteAsync(_Topology, AbortToken);
        await provisioner.DeleteAsync(_Topology, AbortToken);

        // then
        _namespace.Topics.Should().BeEquivalentTo([_TopicPath]);
        _namespace.Subscriptions.Should().BeEmpty();
        _namespace.Queues.Should().BeEquivalentTo([_ReceiptQueue]);
    }

    [Fact]
    public async Task should_report_each_entity_with_its_message_counts()
    {
        // given
        var provisioner = _CreateProvisioner();
        await provisioner.ProvisionAsync(_Topology, AbortToken);
        _namespace.Queues.Remove(_ReceiptQueue);
        _namespace.Messages[_SubscriptionKey] = 5;
        _namespace.Messages[FakeServiceBusNamespace.DeadLetter(_InvoiceQueue)] = 2;

        // when
        var status = await provisioner.GetStatusAsync(_Topology, AbortToken);

        // then
        status
            .Should()
            .Equal(
                new($"topic {_TopicPath}", "exists"),
                new($"subscription {_TopicPath}/{_Subscription}", "5 active, 0 dead-lettered messages"),
                new($"queue {_InvoiceQueue}", "0 active, 2 dead-lettered messages"),
                new($"queue {_ReceiptQueue}", "missing")
            );
    }

    [Fact]
    public async Task should_declare_nothing_shared_from_a_registered_consumer_client()
    {
        // given
        await using var factory = _CreateFactory();

        // when
        var bus = await factory.CreateAsync(new ConsumerClientRequest(_Identity, 1, MessageLane.Bus), AbortToken);
        await bus.SubscribeAsync([_OrderPlaced], AbortToken);
        var queue = await factory.CreateAsync(
            new ConsumerClientRequest(_InvoiceQueue, 1, MessageLane.Queue),
            AbortToken
        );
        await queue.SubscribeAsync([_InvoiceQueue], AbortToken);

        // then
        _namespace.Pool.DidNotReceive().GetAdministrationClient();
        _namespace.Writes.Should().BeEmpty();
    }

    [Fact]
    public async Task should_create_its_own_topology_from_a_runtime_subscription_client()
    {
        // given
        await using var factory = _CreateFactory();

        // when
        var bus = await factory.CreateAsync(
            new ConsumerClientRequest(_Identity, 1, MessageLane.Bus) { ProvisionsTopology = true },
            AbortToken
        );
        await bus.SubscribeAsync([_OrderPlaced], AbortToken);
        var queue = await factory.CreateAsync(
            new ConsumerClientRequest(_InvoiceQueue, 1, MessageLane.Queue) { ProvisionsTopology = true },
            AbortToken
        );
        await queue.SubscribeAsync([_InvoiceQueue], AbortToken);

        // then
        _namespace.Topics.Should().BeEquivalentTo([_TopicPath]);
        _namespace.Subscriptions[_SubscriptionKey].Keys.Should().BeEquivalentTo([_OrderPlaced]);
        _namespace.Queues.Should().BeEquivalentTo([_InvoiceQueue]);
    }

    private static AzureServiceBusMessagingOptions _Options() =>
        new()
        {
            ConnectionString =
                "Endpoint=sb://mynamespace.servicebus.windows.net/;SharedAccessKeyName=myPolicy;SharedAccessKey=myKey",
            TopicPath = _TopicPath,
        };

    private AzureServiceBusTopologyProvisioner _CreateProvisioner(AzureServiceBusMessagingOptions? options = null) =>
        new(
            _namespace.Pool,
            Options.Create(options ?? _Options()),
            NullLogger<AzureServiceBusTopologyProvisioner>.Instance
        );

    private AzureServiceBusConsumerClientFactory _CreateFactory() =>
        new(
            NullLoggerFactory.Instance,
            Options.Create(_Options()),
            new ServiceCollection().BuildServiceProvider(),
            _namespace.Pool
        );
}
