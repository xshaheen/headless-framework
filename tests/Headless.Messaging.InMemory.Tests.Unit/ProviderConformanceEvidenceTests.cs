// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Testing.Tests;
using Tests.Capabilities;

namespace Tests;

public sealed class ProviderConformanceEvidenceTests : TestBase
{
    [Fact]
    public void should_bind_every_supported_manifest_scenario_to_an_existing_test()
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
                TransportConformanceScenario.BusConsumerIdentityFanOut,
                nameof(
                    InMemoryProviderConformanceTests.should_deliver_one_bus_copy_per_consumer_identity_while_replicas_compete
                )
            ),
            _Bind(
                TransportConformanceScenario.BusReplicaCompetition,
                nameof(
                    InMemoryProviderConformanceTests.should_deliver_one_bus_copy_per_consumer_identity_while_replicas_compete
                )
            ),
            _Bind(
                TransportConformanceScenario.QueueOwnership,
                nameof(InMemoryProviderConformanceTests.should_deliver_one_owned_queue_copy_across_replicas)
            ),
            _Bind(
                TransportConformanceScenario.SameNameLaneIsolation,
                nameof(InMemoryProviderConformanceTests.should_isolate_same_logical_name_between_bus_and_queue)
            ),
            _Bind(
                TransportConformanceScenario.RequestReplyRoundTrip,
                nameof(InMemoryProviderConformanceTests.should_return_the_typed_response_of_a_request)
            ),
            _Bind(
                TransportConformanceScenario.RequestReplyCallerIsolation,
                nameof(InMemoryProviderConformanceTests.should_give_each_caller_only_its_own_replies)
            ),
            _Bind(
                TransportConformanceScenario.RequestReplyCallerIsolation,
                nameof(InMemoryProviderConformanceTests.should_never_deliver_a_reply_to_a_restarted_caller)
            ),
            _Bind(
                TransportConformanceScenario.RequestReplyForeignAddressRefusal,
                nameof(InMemoryProviderConformanceTests.should_never_write_a_reply_to_a_foreign_reply_address)
            ),
            _Bind(
                TransportConformanceScenario.RequestReplyCallerCleanup,
                nameof(InMemoryProviderConformanceTests.should_leave_no_reply_objects_after_the_caller_stops)
            ),
        ];

        // Every bound method is itself an xUnit test in this assembly, so it already runs on its own;
        // this test only proves the manifest and the bindings still line up.
        TransportConformanceTestBindings.GetValidationErrors(profile, bindings).Should().BeEmpty();
    }

    private static TransportConformanceTestBinding _Bind(TransportConformanceScenario scenario, string method) =>
        new(scenario, typeof(InMemoryProviderConformanceTests), method);
}
