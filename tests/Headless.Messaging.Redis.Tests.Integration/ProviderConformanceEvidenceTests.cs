// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Testing.Tests;
using Tests.Capabilities;

namespace Tests;

[Collection<RedisMessagingFixture>]
public sealed class ProviderConformanceEvidenceTests(RedisMessagingFixture fixture) : TestBase
{
    [Fact]
    public Task should_prove_routing_affinity_mapping_or_rejection() =>
        TransportRoutingAffinityConformance.AssertAsync(new RedisProviderConformanceDriver(fixture), AbortToken);

    [Fact]
    public void should_back_every_supported_manifest_scenario_with_a_test()
    {
        var profile = TransportConformanceManifest.Providers["Redis"];
        TransportConformanceTestBinding[] bindings =
        [
            new(
                TransportConformanceScenario.RoutingAffinityMappingOrRejection,
                typeof(ProviderConformanceEvidenceTests),
                nameof(should_prove_routing_affinity_mapping_or_rejection)
            ),
            _Bind(
                TransportConformanceScenario.QueueRoundTrip,
                nameof(RedisConsumerConformanceTests.should_round_trip_queue_message_body_and_headers)
            ),
            _Bind(
                TransportConformanceScenario.BusRoundTrip,
                nameof(
                    RedisConsumerConformanceTests.should_deliver_one_bus_copy_per_consumer_identity_while_replicas_compete
                )
            ),
            _Bind(
                TransportConformanceScenario.HeaderRoundTrip,
                nameof(RedisConsumerConformanceTests.should_round_trip_queue_message_body_and_headers)
            ),
            _Bind(
                TransportConformanceScenario.EmptyBodyDispatch,
                nameof(RedisConsumerConformanceTests.should_dispatch_empty_message_body)
            ),
            _Bind(
                TransportConformanceScenario.CommitSettlement,
                nameof(RedisConsumerConformanceTests.should_commit_real_delivery_and_prevent_redelivery)
            ),
            _Bind(
                TransportConformanceScenario.RejectRedelivery,
                nameof(RedisConsumerConformanceTests.should_reject_real_delivery_and_observe_redelivery)
            ),
            _Bind(
                TransportConformanceScenario.BoundedGracefulShutdown,
                nameof(RedisConsumerConformanceTests.should_shutdown_idle_consumer_within_bound)
            ),
            _Bind(
                TransportConformanceScenario.BusConsumerIdentityFanOut,
                nameof(
                    RedisConsumerConformanceTests.should_deliver_one_bus_copy_per_consumer_identity_while_replicas_compete
                )
            ),
            _Bind(
                TransportConformanceScenario.BusReplicaCompetition,
                nameof(
                    RedisConsumerConformanceTests.should_deliver_one_bus_copy_per_consumer_identity_while_replicas_compete
                )
            ),
            _Bind(
                TransportConformanceScenario.QueueOwnership,
                nameof(RedisConsumerConformanceTests.should_deliver_one_owned_queue_copy_across_replicas)
            ),
            _Bind(
                TransportConformanceScenario.SameNameLaneIsolation,
                nameof(RedisConsumerConformanceTests.should_isolate_same_logical_name_between_bus_and_queue)
            ),
            _Bind(
                TransportConformanceScenario.MalformedEnvelopeTerminalSettlement,
                nameof(RedisConsumerConformanceTests.should_terminally_ack_malformed_entry_across_consumer_restart)
            ),
            _BindRequestReply(
                TransportConformanceScenario.RequestReplyRoundTrip,
                nameof(RedisRequestReplyConformanceTests.should_return_the_typed_response_of_a_request)
            ),
            _BindRequestReply(
                TransportConformanceScenario.RequestReplyCallerIsolation,
                nameof(RedisRequestReplyConformanceTests.should_give_each_caller_only_its_own_replies)
            ),
            _BindRequestReply(
                TransportConformanceScenario.RequestReplyCallerIsolation,
                nameof(RedisRequestReplyConformanceTests.should_never_deliver_a_reply_to_a_restarted_caller)
            ),
            _BindRequestReply(
                TransportConformanceScenario.RequestReplyForeignAddressRefusal,
                nameof(RedisRequestReplyConformanceTests.should_never_write_a_reply_to_a_foreign_reply_address)
            ),
            _BindRequestReply(
                TransportConformanceScenario.RequestReplyCallerCleanup,
                nameof(RedisRequestReplyConformanceTests.should_leave_no_reply_objects_after_the_caller_stops)
            ),
        ];

        TransportConformanceTestBindings.GetValidationErrors(profile, bindings).Should().BeEmpty();
    }

    private static TransportConformanceTestBinding _Bind(TransportConformanceScenario scenario, string method) =>
        new(scenario, typeof(RedisConsumerConformanceTests), method);

    private static TransportConformanceTestBinding _BindRequestReply(
        TransportConformanceScenario scenario,
        string method
    ) => new(scenario, typeof(RedisRequestReplyConformanceTests), method);
}
