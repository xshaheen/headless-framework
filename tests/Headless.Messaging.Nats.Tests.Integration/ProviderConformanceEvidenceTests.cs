// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Testing.Tests;
using Tests.Capabilities;

namespace Tests;

[Collection("Nats")]
public sealed class ProviderConformanceEvidenceTests(NatsFixture fixture) : TestBase
{
    [Fact]
    public Task should_prove_routing_affinity_mapping_or_rejection() =>
        TransportRoutingAffinityConformance.AssertAsync(new NatsProviderConformanceDriver(fixture), AbortToken);

    [Fact]
    public async Task should_execute_every_supported_manifest_scenario()
    {
        var profile = TransportConformanceManifest.Providers["NATS"];
        TransportConformanceTestBinding[] bindings =
        [
            new(
                TransportConformanceScenario.RoutingAffinityMappingOrRejection,
                typeof(ProviderConformanceEvidenceTests),
                nameof(should_prove_routing_affinity_mapping_or_rejection)
            ),
            _Bind(
                TransportConformanceScenario.QueueRoundTrip,
                nameof(NatsConsumerClientTests.should_round_trip_queue_message_body_and_headers)
            ),
            _Bind(
                TransportConformanceScenario.BusRoundTrip,
                nameof(NatsConsumerClientTests.should_fan_out_bus_message_to_distinct_real_subscriptions)
            ),
            _Bind(
                TransportConformanceScenario.HeaderRoundTrip,
                nameof(NatsConsumerClientTests.should_round_trip_queue_message_body_and_headers)
            ),
            _Bind(
                TransportConformanceScenario.EmptyBodyDispatch,
                nameof(NatsConsumerClientTests.should_dispatch_empty_message_body)
            ),
            _Bind(
                TransportConformanceScenario.CommitSettlement,
                nameof(NatsConsumerClientTests.should_commit_real_delivery_and_prevent_redelivery)
            ),
            _Bind(
                TransportConformanceScenario.RejectRedelivery,
                nameof(NatsConsumerClientTests.should_reject_real_delivery_and_observe_redelivery)
            ),
            new(
                TransportConformanceScenario.ConsumerPauseRecovery,
                typeof(NatsBrokerFaultTests),
                nameof(NatsBrokerFaultTests.should_resume_delivery_once_after_consumer_pause)
            ),
            _Bind(
                TransportConformanceScenario.BoundedGracefulShutdown,
                nameof(NatsConsumerClientTests.should_shutdown_idle_consumer_within_bound)
            ),
            _Bind(
                TransportConformanceScenario.BusSubscriberGroupFanOut,
                nameof(NatsConsumerClientTests.should_fan_out_one_bus_copy_per_group_while_replicas_compete)
            ),
            _Bind(
                TransportConformanceScenario.BusReplicaCompetition,
                nameof(NatsConsumerClientTests.should_fan_out_one_bus_copy_per_group_while_replicas_compete)
            ),
            _Bind(
                TransportConformanceScenario.QueueOwnership,
                nameof(NatsConsumerClientTests.should_deliver_one_owned_queue_copy_across_replicas)
            ),
            _Bind(
                TransportConformanceScenario.SameNameLaneIsolation,
                nameof(NatsConsumerClientTests.should_isolate_same_logical_name_across_bus_and_queue)
            ),
            _Bind(
                TransportConformanceScenario.MalformedEnvelopeTerminalSettlement,
                nameof(NatsConsumerClientTests.should_terminally_acknowledge_malformed_envelope_across_consumer_restart)
            ),
        ];

        await TransportConformanceTestBindings.ExecuteSupportedScenariosAsync(profile, bindings, _CreateTestClass);
    }

    private static TransportConformanceTestBinding _Bind(TransportConformanceScenario scenario, string method) =>
        new(scenario, typeof(NatsConsumerClientTests), method);

    private object _CreateTestClass(Type testClass)
    {
        if (testClass == typeof(ProviderConformanceEvidenceTests))
        {
            return new ProviderConformanceEvidenceTests(fixture);
        }

        if (testClass == typeof(NatsConsumerClientTests))
        {
            return new NatsConsumerClientTests(fixture);
        }

        if (testClass == typeof(NatsBrokerFaultTests))
        {
            return new NatsBrokerFaultTests(fixture);
        }

        throw new InvalidOperationException($"No NATS conformance test factory is registered for {testClass}.");
    }
}
