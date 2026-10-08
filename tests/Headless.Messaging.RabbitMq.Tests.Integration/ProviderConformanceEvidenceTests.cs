// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Testing.Tests;
using Tests.Capabilities;

namespace Tests;

[Collection<RabbitMqFixture>]
public sealed class ProviderConformanceEvidenceTests(RabbitMqFixture fixture) : TestBase
{
    [Fact]
    public Task should_prove_routing_affinity_mapping_or_rejection() =>
        TransportRoutingAffinityConformance.AssertAsync(new RabbitMqProviderConformanceDriver(fixture), AbortToken);

    [Fact]
    public void should_back_every_supported_manifest_scenario_with_a_test()
    {
        var profile = TransportConformanceManifest.Providers["RabbitMQ"];
        TransportConformanceTestBinding[] bindings =
        [
            new(
                TransportConformanceScenario.RoutingAffinityMappingOrRejection,
                typeof(ProviderConformanceEvidenceTests),
                nameof(should_prove_routing_affinity_mapping_or_rejection)
            ),
            _Bind(
                TransportConformanceScenario.QueueRoundTrip,
                nameof(RabbitMqConsumerClientConformanceTests.should_round_trip_queue_message_body_and_headers)
            ),
            _Bind(
                TransportConformanceScenario.BusRoundTrip,
                nameof(RabbitMqConsumerClientConformanceTests.should_fan_out_bus_message_to_distinct_real_subscriptions)
            ),
            _Bind(
                TransportConformanceScenario.HeaderRoundTrip,
                nameof(RabbitMqConsumerClientConformanceTests.should_round_trip_queue_message_body_and_headers)
            ),
            _Bind(
                TransportConformanceScenario.EmptyBodyDispatch,
                nameof(RabbitMqConsumerClientConformanceTests.should_dispatch_empty_message_body)
            ),
            _Bind(
                TransportConformanceScenario.CommitSettlement,
                nameof(RabbitMqConsumerClientConformanceTests.should_commit_real_delivery_and_prevent_redelivery)
            ),
            _Bind(
                TransportConformanceScenario.RejectRedelivery,
                nameof(RabbitMqConsumerClientConformanceTests.should_reject_real_delivery_and_observe_redelivery)
            ),
            new(
                TransportConformanceScenario.ConsumerPauseRecovery,
                typeof(RabbitMqBrokerFaultTests),
                nameof(RabbitMqBrokerFaultTests.should_resume_delivery_once_after_consumer_pause)
            ),
            _Bind(
                TransportConformanceScenario.BoundedGracefulShutdown,
                nameof(RabbitMqConsumerClientConformanceTests.should_shutdown_idle_consumer_within_bound)
            ),
            _Bind(
                TransportConformanceScenario.BusConsumerIdentityFanOut,
                nameof(
                    RabbitMqConsumerClientConformanceTests.should_fan_out_one_bus_copy_per_consumer_identity_while_replicas_compete
                )
            ),
            _Bind(
                TransportConformanceScenario.BusReplicaCompetition,
                nameof(
                    RabbitMqConsumerClientConformanceTests.should_fan_out_one_bus_copy_per_consumer_identity_while_replicas_compete
                )
            ),
            _Bind(
                TransportConformanceScenario.QueueOwnership,
                nameof(RabbitMqConsumerClientConformanceTests.should_deliver_one_owned_queue_copy_across_replicas)
            ),
            _Bind(
                TransportConformanceScenario.SameNameLaneIsolation,
                nameof(RabbitMqConsumerClientConformanceTests.should_isolate_same_logical_name_across_bus_and_queue)
            ),
            _Bind(
                TransportConformanceScenario.MalformedEnvelopeTerminalSettlement,
                nameof(
                    RabbitMqConsumerClientConformanceTests.should_terminally_reject_malformed_envelope_across_consumer_restart
                )
            ),
            _BindRequestReply(
                TransportConformanceScenario.RequestReplyRoundTrip,
                nameof(RabbitMqRequestReplyConformanceTests.should_return_the_typed_response_of_a_request)
            ),
            _BindRequestReply(
                TransportConformanceScenario.RequestReplyCallerIsolation,
                nameof(RabbitMqRequestReplyConformanceTests.should_give_each_caller_only_its_own_replies)
            ),
            _BindRequestReply(
                TransportConformanceScenario.RequestReplyCallerIsolation,
                nameof(RabbitMqRequestReplyConformanceTests.should_never_deliver_a_reply_to_a_restarted_caller)
            ),
            _BindRequestReply(
                TransportConformanceScenario.RequestReplyForeignAddressRefusal,
                nameof(RabbitMqRequestReplyConformanceTests.should_never_write_a_reply_to_a_foreign_reply_address)
            ),
            _BindRequestReply(
                TransportConformanceScenario.RequestReplyCallerCleanup,
                nameof(RabbitMqRequestReplyConformanceTests.should_leave_no_reply_objects_after_the_caller_stops)
            ),
        ];

        TransportConformanceTestBindings.GetValidationErrors(profile, bindings).Should().BeEmpty();
    }

    private static TransportConformanceTestBinding _Bind(TransportConformanceScenario scenario, string method) =>
        new(scenario, typeof(RabbitMqConsumerClientConformanceTests), method);

    private static TransportConformanceTestBinding _BindRequestReply(
        TransportConformanceScenario scenario,
        string method
    ) => new(scenario, typeof(RabbitMqRequestReplyConformanceTests), method);
}
