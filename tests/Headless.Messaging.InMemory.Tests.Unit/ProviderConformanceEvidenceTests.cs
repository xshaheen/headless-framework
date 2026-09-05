// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Testing.Tests;
using Tests.Capabilities;

namespace Tests;

public sealed class ProviderConformanceEvidenceTests : TestBase
{
    [Fact]
    public async Task should_execute_every_supported_manifest_scenario()
    {
        var profile = TransportConformanceManifest.Providers["InMemory"];
        TransportConformanceTestBinding[] bindings =
        [
            new(
                TransportConformanceScenario.RoutingAffinityMappingOrRejection,
                typeof(InMemoryProviderConformanceTests),
                nameof(InMemoryProviderConformanceTests.should_prove_routing_affinity_mapping_or_rejection)
            ),
            new(
                TransportConformanceScenario.QueueRoundTrip,
                typeof(InMemoryQueueTransportTests),
                nameof(InMemoryQueueTransportTests.should_send_message_body)
            ),
            new(
                TransportConformanceScenario.BusRoundTrip,
                typeof(InMemoryBusTransportTests),
                nameof(InMemoryBusTransportTests.should_fan_out_message_to_every_subscribed_group)
            ),
            new(
                TransportConformanceScenario.HeaderRoundTrip,
                typeof(InMemoryQueueTransportTests),
                nameof(InMemoryQueueTransportTests.should_support_message_headers)
            ),
            new(
                TransportConformanceScenario.EmptyBodyDispatch,
                typeof(InMemoryQueueTransportTests),
                nameof(InMemoryQueueTransportTests.should_support_message_headers)
            ),
            new(
                TransportConformanceScenario.CommitSettlement,
                typeof(InMemoryConsumerClientTests),
                nameof(InMemoryConsumerClientTests.should_commit_and_release_semaphore)
            ),
            new(
                TransportConformanceScenario.RejectRedelivery,
                typeof(InMemoryConsumerClientTests),
                nameof(InMemoryConsumerClientTests.should_reject_and_release_semaphore)
            ),
            new(
                TransportConformanceScenario.BoundedGracefulShutdown,
                typeof(InMemoryConsumerClientTests),
                nameof(InMemoryConsumerClientTests.should_stop_listening_on_cancellation)
            ),
            _Bind(
                TransportConformanceScenario.BusSubscriberGroupFanOut,
                nameof(InMemoryProviderConformanceTests.should_deliver_one_bus_copy_per_group_while_replicas_compete)
            ),
            _Bind(
                TransportConformanceScenario.BusReplicaCompetition,
                nameof(InMemoryProviderConformanceTests.should_deliver_one_bus_copy_per_group_while_replicas_compete)
            ),
            _Bind(
                TransportConformanceScenario.QueueOwnership,
                nameof(InMemoryProviderConformanceTests.should_deliver_one_owned_queue_copy_across_replicas)
            ),
            _Bind(
                TransportConformanceScenario.SameNameLaneIsolation,
                nameof(InMemoryProviderConformanceTests.should_isolate_same_logical_name_between_bus_and_queue)
            ),
        ];

        await TransportConformanceTestBindings.ExecuteSupportedScenariosAsync(
            profile,
            bindings,
            testClass =>
                Activator.CreateInstance(testClass)
                ?? throw new InvalidOperationException(
                    $"No InMemory conformance test factory is registered for {testClass}."
                )
        );
    }

    private static TransportConformanceTestBinding _Bind(TransportConformanceScenario scenario, string method) =>
        new(scenario, typeof(InMemoryProviderConformanceTests), method);
}
