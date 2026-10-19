// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Testing.Tests;
using Tests.Capabilities;
using Tests.Guarantees;

namespace Tests;

[Collection<RabbitMqFixture>]
public sealed class ProviderConformanceEvidenceTests(RabbitMqFixture fixture) : TestBase
{
    [Fact]
    public void should_run_every_guarantee_this_provider_inherits() =>
        GuaranteeBindings.GetLeafErrors(typeof(ProviderConformanceEvidenceTests).Assembly).Should().BeEmpty();

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
                TransportConformanceScenario.ConnectionDropRecovery,
                typeof(RabbitMqNetworkFaultTests),
                nameof(RabbitMqNetworkFaultTests.should_resume_delivery_once_after_the_connection_is_dropped)
            ),
            new(
                TransportConformanceScenario.RawPayloadConsumption,
                typeof(RabbitMqRawPayloadTests),
                nameof(RabbitMqRawPayloadTests.should_run_the_handler_once_when_a_raw_message_id_is_delivered_twice)
            ),
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
                TransportConformanceScenario.StaleSettlement,
                nameof(
                    RabbitMqConsumerClientConformanceTests.should_not_settle_another_delivery_when_a_settled_value_is_used_again
                )
            ),
            _Bind(
                TransportConformanceScenario.HandlerFailureRedelivery,
                nameof(
                    RabbitMqConsumerClientConformanceTests.should_keep_the_delivery_and_the_listener_when_the_callback_throws
                )
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
            _Bind(
                TransportConformanceScenario.HeaderBuilderFailureRedelivery,
                nameof(
                    RabbitMqConsumerClientConformanceTests.should_redeliver_then_give_up_when_the_header_builder_fails_transiently
                )
            ),
            _Bind(
                TransportConformanceScenario.DeadLetterSingleCopy,
                nameof(
                    RabbitMqConsumerClientConformanceTests.should_keep_one_dead_letter_copy_and_settle_the_original_once
                )
            ),
            _Bind(
                TransportConformanceScenario.StandardHeaderRoundTrip,
                nameof(RabbitMqConsumerClientConformanceTests.should_round_trip_every_standard_header_unchanged)
            ),
            _Bind(
                TransportConformanceScenario.HandlerConcurrencyCap,
                nameof(
                    RabbitMqConsumerClientConformanceTests.should_never_hand_over_more_deliveries_at_once_than_the_requested_concurrency
                )
            ),
            _Bind(
                TransportConformanceScenario.BodySizeLimit,
                nameof(
                    RabbitMqConsumerClientConformanceTests.should_round_trip_a_body_at_the_size_limit_and_refuse_one_byte_more
                )
            ),
            _Bind(
                TransportConformanceScenario.MessageExpiry,
                nameof(
                    RabbitMqConsumerClientConformanceTests.should_not_deliver_a_message_that_expired_before_it_was_consumed
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
