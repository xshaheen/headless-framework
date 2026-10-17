// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Messaging;
using Tests.Simulation;

namespace Tests.Contracts;

/// <summary>
/// The broker-observed delivery and settlement suite, and the shared lane, ownership, and routing assertions, run
/// unchanged against the simulated broker, so the fast tier holds the simulator to what real broker leaves prove.
/// </summary>
public sealed class SimulatorConsumerConformanceTests : TransportConsumerConformanceTestsBase
{
    private readonly SimulatorConformance _simulator = new();

    protected override string ProviderName => SimulatedBroker.ProviderName;

    protected override void ConfigureTransport(MessagingSetupBuilder setup) =>
        setup.UseSimulatedBroker(_simulator.Broker);

    protected override ValueTask<TransportConsumerConformanceSession> CreateSessionAsync(
        CancellationToken cancellationToken
    ) => _simulator.CreateQueueSessionAsync(cancellationToken);

    [Fact]
    public override Task should_match_production_runtime_capabilities() =>
        base.should_match_production_runtime_capabilities();

    [Fact]
    public override Task should_round_trip_queue_message_body_and_headers() =>
        base.should_round_trip_queue_message_body_and_headers();

    [Fact]
    public override Task should_dispatch_empty_message_body() => base.should_dispatch_empty_message_body();

    [Fact]
    public override Task should_commit_real_delivery_and_prevent_redelivery() =>
        base.should_commit_real_delivery_and_prevent_redelivery();

    [Fact]
    public override Task should_reject_real_delivery_and_observe_redelivery() =>
        base.should_reject_real_delivery_and_observe_redelivery();

    [Fact]
    public override Task should_not_settle_another_delivery_when_a_settled_value_is_used_again() =>
        base.should_not_settle_another_delivery_when_a_settled_value_is_used_again();

    [Fact]
    public override Task should_keep_the_delivery_and_the_listener_when_the_callback_throws() =>
        base.should_keep_the_delivery_and_the_listener_when_the_callback_throws();

    [Fact]
    public override Task should_isolate_unique_destinations() => base.should_isolate_unique_destinations();

    [Fact]
    public override Task should_shutdown_idle_consumer_within_bound() =>
        base.should_shutdown_idle_consumer_within_bound();

    [Fact]
    public override Task should_bound_shutdown_while_handler_is_active() =>
        base.should_bound_shutdown_while_handler_is_active();

    [Fact]
    public Task should_prove_routing_affinity_mapping_or_rejection() =>
        TransportRoutingAffinityConformance.AssertAsync(_simulator.Driver, AbortToken);

    [Fact]
    public Task should_deliver_one_bus_copy_per_consumer_identity_while_replicas_compete() =>
        TransportProviderConformance.AssertBusConsumerIdentitiesAsync(_simulator.Driver, AbortToken);

    [Fact]
    public Task should_deliver_every_bus_message_to_every_every_instance_replica() =>
        TransportProviderConformance.AssertBusEveryInstanceAsync(_simulator.Driver, AbortToken);

    [Fact]
    public Task should_deliver_one_owned_queue_copy_across_replicas() =>
        TransportProviderConformance.AssertQueueOwnershipAsync(_simulator.Driver, AbortToken);

    [Fact]
    public Task should_isolate_same_logical_name_between_bus_and_queue() =>
        TransportProviderConformance.AssertSameNameLaneIsolationAsync(_simulator.Driver, AbortToken);

    protected override async ValueTask DisposeAsyncCore()
    {
        await _simulator.DisposeAsync();
        await base.DisposeAsyncCore();
    }
}
