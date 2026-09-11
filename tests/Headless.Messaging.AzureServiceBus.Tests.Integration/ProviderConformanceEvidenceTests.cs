// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Testing.Tests;
using Tests.Capabilities;

namespace Tests;

[Collection("AzureServiceBus")]
public sealed class ProviderConformanceEvidenceTests(AzureServiceBusFixture fixture) : TestBase
{
    [Fact]
    public Task should_prove_routing_affinity_mapping_or_rejection() =>
        TransportRoutingAffinityConformance.AssertAsync(
            new AzureServiceBusProviderConformanceDriver(fixture),
            AbortToken
        );

    [Fact]
    public async Task should_execute_every_supported_manifest_scenario()
    {
        var profile = TransportConformanceManifest.Providers["Azure Service Bus"];
        TransportConformanceTestBinding[] bindings =
        [
            new(
                TransportConformanceScenario.RoutingAffinityMappingOrRejection,
                typeof(ProviderConformanceEvidenceTests),
                nameof(should_prove_routing_affinity_mapping_or_rejection)
            ),
            _Bind(
                TransportConformanceScenario.QueueRoundTrip,
                nameof(AzureServiceBusConsumerClientHarnessTests.should_round_trip_queue_message_body_and_headers)
            ),
            new(
                TransportConformanceScenario.BusRoundTrip,
                typeof(AzureServiceBusTransportTests),
                nameof(AzureServiceBusTransportTests.should_fan_out_bus_delivery_to_distinct_subscriptions)
            ),
            _Bind(
                TransportConformanceScenario.HeaderRoundTrip,
                nameof(AzureServiceBusConsumerClientHarnessTests.should_round_trip_queue_message_body_and_headers)
            ),
            _Bind(
                TransportConformanceScenario.CommitSettlement,
                nameof(AzureServiceBusConsumerClientHarnessTests.should_commit_real_delivery_and_prevent_redelivery)
            ),
            _Bind(
                TransportConformanceScenario.RejectRedelivery,
                nameof(AzureServiceBusConsumerClientHarnessTests.should_reject_real_delivery_and_observe_redelivery)
            ),
            new(
                TransportConformanceScenario.ConsumerPauseRecovery,
                typeof(AzureServiceBusBrokerFaultTests),
                nameof(AzureServiceBusBrokerFaultTests.should_resume_delivery_once_after_consumer_pause)
            ),
            _Bind(
                TransportConformanceScenario.BoundedGracefulShutdown,
                nameof(AzureServiceBusConsumerClientHarnessTests.should_shutdown_idle_consumer_within_bound)
            ),
            _Bind(
                TransportConformanceScenario.BusSubscriberGroupFanOut,
                nameof(
                    AzureServiceBusConsumerClientHarnessTests.should_deliver_one_bus_copy_per_group_while_replicas_compete
                )
            ),
            _Bind(
                TransportConformanceScenario.BusReplicaCompetition,
                nameof(
                    AzureServiceBusConsumerClientHarnessTests.should_deliver_one_bus_copy_per_group_while_replicas_compete
                )
            ),
            _Bind(
                TransportConformanceScenario.QueueOwnership,
                nameof(AzureServiceBusConsumerClientHarnessTests.should_deliver_one_owned_queue_copy_across_replicas)
            ),
            _Bind(
                TransportConformanceScenario.SameNameLaneIsolation,
                nameof(AzureServiceBusConsumerClientHarnessTests.should_isolate_same_logical_name_between_bus_and_queue)
            ),
        ];

        await TransportConformanceTestBindings.ExecuteSupportedScenariosAsync(profile, bindings, _CreateTestClass);
    }

    private static TransportConformanceTestBinding _Bind(TransportConformanceScenario scenario, string method) =>
        new(scenario, typeof(AzureServiceBusConsumerClientHarnessTests), method);

    private object _CreateTestClass(Type testClass)
    {
        if (testClass == typeof(ProviderConformanceEvidenceTests))
        {
            return new ProviderConformanceEvidenceTests(fixture);
        }

        if (testClass == typeof(AzureServiceBusConsumerClientHarnessTests))
        {
            return new AzureServiceBusConsumerClientHarnessTests(fixture);
        }

        if (testClass == typeof(AzureServiceBusTransportTests))
        {
            return new AzureServiceBusTransportTests(fixture);
        }

        if (testClass == typeof(AzureServiceBusBrokerFaultTests))
        {
            return new AzureServiceBusBrokerFaultTests(fixture);
        }

        throw new InvalidOperationException(
            $"No Azure Service Bus conformance test factory is registered for {testClass}."
        );
    }
}
