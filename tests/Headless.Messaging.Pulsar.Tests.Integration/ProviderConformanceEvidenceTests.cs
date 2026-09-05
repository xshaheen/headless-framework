// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Testing.Tests;
using Tests.Capabilities;

namespace Tests;

[Collection("Pulsar")]
public sealed class ProviderConformanceEvidenceTests(PulsarFixture fixture) : TestBase
{
    [Fact]
    public Task should_prove_routing_affinity_mapping_or_rejection() =>
        TransportRoutingAffinityConformance.AssertAsync(new PulsarProviderConformanceDriver(fixture), AbortToken);

    [Fact]
    public async Task should_execute_every_supported_manifest_scenario()
    {
        var profile = TransportConformanceManifest.Providers["Pulsar"];
        TransportConformanceTestBinding[] bindings =
        [
            new(
                TransportConformanceScenario.RoutingAffinityMappingOrRejection,
                typeof(ProviderConformanceEvidenceTests),
                nameof(should_prove_routing_affinity_mapping_or_rejection)
            ),
            _Bind(
                TransportConformanceScenario.QueueRoundTrip,
                nameof(PulsarConsumerClientHarnessTests.should_round_trip_queue_message_body_and_headers)
            ),
            new(
                TransportConformanceScenario.BusRoundTrip,
                typeof(PulsarTransportTests),
                nameof(PulsarTransportTests.should_fan_out_bus_delivery_to_distinct_subscriptions)
            ),
            _Bind(
                TransportConformanceScenario.HeaderRoundTrip,
                nameof(PulsarConsumerClientHarnessTests.should_round_trip_queue_message_body_and_headers)
            ),
            _Bind(
                TransportConformanceScenario.CommitSettlement,
                nameof(PulsarConsumerClientHarnessTests.should_commit_real_delivery_and_prevent_redelivery)
            ),
            _Bind(
                TransportConformanceScenario.RejectRedelivery,
                nameof(PulsarConsumerClientHarnessTests.should_reject_real_delivery_and_observe_redelivery)
            ),
            new(
                TransportConformanceScenario.ConsumerPauseRecovery,
                typeof(PulsarBrokerFaultTests),
                nameof(PulsarBrokerFaultTests.should_resume_delivery_once_after_consumer_pause)
            ),
            _Bind(
                TransportConformanceScenario.BoundedGracefulShutdown,
                nameof(PulsarConsumerClientHarnessTests.should_shutdown_idle_consumer_within_bound)
            ),
            _Bind(
                TransportConformanceScenario.BusSubscriberGroupFanOut,
                nameof(PulsarConsumerClientHarnessTests.should_fan_out_one_bus_copy_per_group_while_replicas_compete)
            ),
            _Bind(
                TransportConformanceScenario.BusReplicaCompetition,
                nameof(PulsarConsumerClientHarnessTests.should_fan_out_one_bus_copy_per_group_while_replicas_compete)
            ),
            _Bind(
                TransportConformanceScenario.QueueOwnership,
                nameof(PulsarConsumerClientHarnessTests.should_deliver_one_owned_queue_copy_across_replicas)
            ),
            _Bind(
                TransportConformanceScenario.SameNameLaneIsolation,
                nameof(PulsarConsumerClientHarnessTests.should_isolate_same_logical_name_across_bus_and_queue)
            ),
            _Bind(
                TransportConformanceScenario.MalformedEnvelopeTerminalSettlement,
                nameof(
                    PulsarConsumerClientHarnessTests.should_terminally_acknowledge_malformed_envelope_across_consumer_restart
                )
            ),
        ];

        await TransportConformanceTestBindings.ExecuteSupportedScenariosAsync(profile, bindings, _CreateTestClass);
    }

    private static TransportConformanceTestBinding _Bind(TransportConformanceScenario scenario, string method) =>
        new(scenario, typeof(PulsarConsumerClientHarnessTests), method);

    private object _CreateTestClass(Type testClass)
    {
        if (testClass == typeof(ProviderConformanceEvidenceTests))
        {
            return new ProviderConformanceEvidenceTests(fixture);
        }

        if (testClass == typeof(PulsarConsumerClientHarnessTests))
        {
            return new PulsarConsumerClientHarnessTests(fixture);
        }

        if (testClass == typeof(PulsarTransportTests))
        {
            return new PulsarTransportTests(fixture);
        }

        if (testClass == typeof(PulsarBrokerFaultTests))
        {
            return new PulsarBrokerFaultTests(fixture);
        }

        throw new InvalidOperationException($"No Pulsar conformance test factory is registered for {testClass}.");
    }
}
