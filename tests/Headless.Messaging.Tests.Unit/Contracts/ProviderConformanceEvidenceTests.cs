// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Testing.Tests;
using Tests.Capabilities;

namespace Tests.Contracts;

public sealed class ProviderConformanceEvidenceTests : TestBase
{
    [Fact]
    public void should_bind_every_supported_simulator_scenario_to_an_existing_test()
    {
        TransportConformanceTestBinding[] bindings =
        [
            _Consumer(
                TransportConformanceScenario.RoutingAffinityMappingOrRejection,
                nameof(SimulatorConsumerConformanceTests.should_prove_routing_affinity_mapping_or_rejection)
            ),
            _Consumer(
                TransportConformanceScenario.QueueRoundTrip,
                nameof(SimulatorConsumerConformanceTests.should_round_trip_queue_message_body_and_headers)
            ),
            _Consumer(
                TransportConformanceScenario.BusRoundTrip,
                nameof(
                    SimulatorConsumerConformanceTests.should_deliver_one_bus_copy_per_consumer_identity_while_replicas_compete
                )
            ),
            _Consumer(
                TransportConformanceScenario.HeaderRoundTrip,
                nameof(SimulatorConsumerConformanceTests.should_round_trip_queue_message_body_and_headers)
            ),
            _Consumer(
                TransportConformanceScenario.EmptyBodyDispatch,
                nameof(SimulatorConsumerConformanceTests.should_dispatch_empty_message_body)
            ),
            _Consumer(
                TransportConformanceScenario.CommitSettlement,
                nameof(SimulatorConsumerConformanceTests.should_commit_real_delivery_and_prevent_redelivery)
            ),
            _Consumer(
                TransportConformanceScenario.RejectRedelivery,
                nameof(SimulatorConsumerConformanceTests.should_reject_real_delivery_and_observe_redelivery)
            ),
            new(
                TransportConformanceScenario.ConsumerPauseRecovery,
                typeof(SimulatorBrokerFaultTests),
                nameof(SimulatorBrokerFaultTests.should_resume_delivery_once_after_consumer_pause)
            ),
            new(
                TransportConformanceScenario.BrokerInterruptionRecovery,
                typeof(SimulatorBrokerFaultTests),
                nameof(SimulatorBrokerFaultTests.should_resume_delivery_once_after_broker_restart)
            ),
            new(
                TransportConformanceScenario.ConnectionDropRecovery,
                typeof(SimulatorNetworkFaultTests),
                nameof(SimulatorNetworkFaultTests.should_resume_delivery_once_after_the_connection_is_dropped)
            ),
            new(
                TransportConformanceScenario.PublishDuringOutage,
                typeof(SimulatorNetworkFaultTests),
                nameof(SimulatorNetworkFaultTests.should_keep_consuming_after_publishes_during_an_outage)
            ),
            _Consumer(
                TransportConformanceScenario.StaleSettlement,
                nameof(
                    SimulatorConsumerConformanceTests.should_not_settle_another_delivery_when_a_settled_value_is_used_again
                )
            ),
            _Consumer(
                TransportConformanceScenario.HandlerFailureRedelivery,
                nameof(
                    SimulatorConsumerConformanceTests.should_keep_the_delivery_and_the_listener_when_the_callback_throws
                )
            ),
            _Consumer(
                TransportConformanceScenario.BoundedGracefulShutdown,
                nameof(SimulatorConsumerConformanceTests.should_bound_shutdown_while_handler_is_active)
            ),
            _Consumer(
                TransportConformanceScenario.BusConsumerIdentityFanOut,
                nameof(
                    SimulatorConsumerConformanceTests.should_deliver_one_bus_copy_per_consumer_identity_while_replicas_compete
                )
            ),
            _Consumer(
                TransportConformanceScenario.BusReplicaCompetition,
                nameof(
                    SimulatorConsumerConformanceTests.should_deliver_one_bus_copy_per_consumer_identity_while_replicas_compete
                )
            ),
            _Consumer(
                TransportConformanceScenario.QueueOwnership,
                nameof(SimulatorConsumerConformanceTests.should_deliver_one_owned_queue_copy_across_replicas)
            ),
            _Consumer(
                TransportConformanceScenario.SameNameLaneIsolation,
                nameof(SimulatorConsumerConformanceTests.should_isolate_same_logical_name_between_bus_and_queue)
            ),
            _Consumer(
                TransportConformanceScenario.DeadLetterSingleCopy,
                nameof(SimulatorConsumerConformanceTests.should_keep_one_dead_letter_copy_and_settle_the_original_once)
            ),
            _Consumer(
                TransportConformanceScenario.StandardHeaderRoundTrip,
                nameof(SimulatorConsumerConformanceTests.should_round_trip_every_standard_header_unchanged)
            ),
            _Consumer(
                TransportConformanceScenario.HandlerConcurrencyCap,
                nameof(
                    SimulatorConsumerConformanceTests.should_never_hand_over_more_deliveries_at_once_than_the_requested_concurrency
                )
            ),
            _RequestReply(
                TransportConformanceScenario.RequestReplyRoundTrip,
                nameof(SimulatorRequestReplyConformanceTests.should_return_the_typed_response_of_a_request)
            ),
            _RequestReply(
                TransportConformanceScenario.RequestReplyCallerIsolation,
                nameof(SimulatorRequestReplyConformanceTests.should_give_each_caller_only_its_own_replies)
            ),
            _RequestReply(
                TransportConformanceScenario.RequestReplyCallerIsolation,
                nameof(SimulatorRequestReplyConformanceTests.should_never_deliver_a_reply_to_a_restarted_caller)
            ),
            _RequestReply(
                TransportConformanceScenario.RequestReplyForeignAddressRefusal,
                nameof(SimulatorRequestReplyConformanceTests.should_never_write_a_reply_to_a_foreign_reply_address)
            ),
            _RequestReply(
                TransportConformanceScenario.RequestReplyCallerCleanup,
                nameof(SimulatorRequestReplyConformanceTests.should_leave_no_reply_objects_after_the_caller_stops)
            ),
        ];

        // Every bound method is itself an xUnit test in this assembly, so it already runs on its own;
        // this test only proves the manifest and the bindings still line up.
        TransportConformanceTestBindings
            .GetValidationErrors(TransportConformanceManifest.Simulator, bindings)
            .Should()
            .BeEmpty();
    }

    private static TransportConformanceTestBinding _Consumer(TransportConformanceScenario scenario, string method) =>
        new(scenario, typeof(SimulatorConsumerConformanceTests), method);

    private static TransportConformanceTestBinding _RequestReply(
        TransportConformanceScenario scenario,
        string method
    ) => new(scenario, typeof(SimulatorRequestReplyConformanceTests), method);
}
