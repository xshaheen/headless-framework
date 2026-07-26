// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Messaging;
using Headless.Messaging.Configuration;

namespace Tests;

[Collection("AzureServiceBus")]
public sealed class AzureServiceBusConsumerClientHarnessTests(AzureServiceBusFixture fixture)
    : TransportConsumerConformanceTestsBase
{
    protected override string ProviderName => "Azure Service Bus";

    protected override void ConfigureTransport(MessagingSetupBuilder setup)
    {
        setup.UseAzureServiceBus(fixture.ConnectionString);
    }

    protected override ValueTask<TransportConsumerConformanceSession> CreateSessionAsync(
        CancellationToken cancellationToken
    )
    {
        return fixture.CreateQueueSessionAsync(cancellationToken);
    }

    [Fact]
    public override Task should_round_trip_queue_message_body_and_headers()
    {
        return base.should_round_trip_queue_message_body_and_headers();
    }

    [Fact]
    public override Task should_match_production_runtime_capabilities()
    {
        return base.should_match_production_runtime_capabilities();
    }

    [Fact]
    public override Task should_commit_real_delivery_and_prevent_redelivery()
    {
        return base.should_commit_real_delivery_and_prevent_redelivery();
    }

    [Fact]
    public override Task should_reject_real_delivery_and_observe_redelivery()
    {
        return base.should_reject_real_delivery_and_observe_redelivery();
    }

    [Fact]
    public override Task should_isolate_unique_destinations()
    {
        return base.should_isolate_unique_destinations();
    }

    [Fact]
    public override Task should_shutdown_idle_consumer_within_bound()
    {
        return base.should_shutdown_idle_consumer_within_bound();
    }

    [Fact]
    public override Task should_bound_shutdown_while_handler_is_active()
    {
        return base.should_bound_shutdown_while_handler_is_active();
    }

    [Fact]
    public Task should_deliver_one_bus_copy_per_group_while_replicas_compete()
    {
        return TransportProviderConformance.AssertBusSubscriberGroupsAsync(
            new AzureServiceBusProviderConformanceDriver(fixture),
            AbortToken
        );
    }

    [Fact]
    public Task should_deliver_one_owned_queue_copy_across_replicas()
    {
        return TransportProviderConformance.AssertQueueOwnershipAsync(
            new AzureServiceBusProviderConformanceDriver(fixture),
            AbortToken
        );
    }

    [Fact]
    public Task should_isolate_same_logical_name_between_bus_and_queue()
    {
        return TransportProviderConformance.AssertSameNameLaneIsolationAsync(
            new AzureServiceBusProviderConformanceDriver(fixture),
            AbortToken
        );
    }
}
