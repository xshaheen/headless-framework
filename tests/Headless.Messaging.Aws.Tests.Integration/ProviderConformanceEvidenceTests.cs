// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Testing.Tests;
using Tests.Capabilities;
using Tests.Guarantees;
using Tests.RequestReply;

namespace Tests;

[Collection<LocalStackTestFixture>]
public sealed class ProviderConformanceEvidenceTests(LocalStackTestFixture fixture) : TestBase
{
    [Fact]
    public void should_run_every_guarantee_this_provider_inherits() =>
        GuaranteeBindings.GetLeafErrors(typeof(ProviderConformanceEvidenceTests).Assembly).Should().BeEmpty();

    [Fact]
    public Task should_prove_routing_affinity_mapping_or_rejection() =>
        TransportRoutingAffinityConformance.AssertAsync(new AwsProviderConformanceDriver(fixture), AbortToken);

    [Fact]
    public Task should_reject_requests_and_responders_at_startup_on_a_transport_without_request_reply() =>
        TransportRequestReplyConformance.AssertRejectedAtStartupAsync(
            new AwsProviderConformanceDriver(fixture),
            AbortToken
        );

    [Fact]
    public void should_back_every_supported_manifest_scenario_with_a_test()
    {
        var profile = TransportConformanceManifest.Providers["AWS/LocalStack"];
        TransportConformanceTestBinding[] bindings =
        [
            new(
                TransportConformanceScenario.ConnectionDropRecovery,
                typeof(AmazonSqsNetworkFaultTests),
                nameof(AmazonSqsNetworkFaultTests.should_stop_consuming_while_the_proxy_is_cut_and_resume_once_after)
            ),
            new(
                TransportConformanceScenario.ConnectionDropRecovery,
                typeof(AmazonSqsNetworkFaultTests),
                nameof(AmazonSqsNetworkFaultTests.should_resume_delivery_once_after_the_connection_is_dropped)
            ),
            new(
                TransportConformanceScenario.PublishDuringOutage,
                typeof(AmazonSqsNetworkFaultTests),
                nameof(AmazonSqsNetworkFaultTests.should_keep_consuming_after_publishes_during_an_outage)
            ),
            new(
                TransportConformanceScenario.RawPayloadConsumption,
                typeof(AmazonSqsRawPayloadTests),
                nameof(
                    AmazonSqsRawPayloadTests.should_consume_a_headerless_queue_message_once_across_a_redelivery_and_poison_a_malformed_body
                )
            ),
            new(
                TransportConformanceScenario.RoutingAffinityMappingOrRejection,
                typeof(ProviderConformanceEvidenceTests),
                nameof(should_prove_routing_affinity_mapping_or_rejection)
            ),
            _Bind(
                TransportConformanceScenario.QueueRoundTrip,
                nameof(AmazonSqsConsumerClientConformanceTests.should_round_trip_queue_message_body_and_headers)
            ),
            _Bind(
                TransportConformanceScenario.BusRoundTrip,
                nameof(
                    AmazonSqsConsumerClientConformanceTests.should_fan_out_bus_message_to_distinct_real_subscriptions
                )
            ),
            _Bind(
                TransportConformanceScenario.HeaderRoundTrip,
                nameof(AmazonSqsConsumerClientConformanceTests.should_round_trip_queue_message_body_and_headers)
            ),
            _Bind(
                TransportConformanceScenario.CommitSettlement,
                nameof(AmazonSqsConsumerClientConformanceTests.should_commit_real_delivery_and_prevent_redelivery)
            ),
            _Bind(
                TransportConformanceScenario.RejectRedelivery,
                nameof(AmazonSqsConsumerClientConformanceTests.should_reject_real_delivery_and_observe_redelivery)
            ),
            new(
                TransportConformanceScenario.ConsumerPauseRecovery,
                typeof(AmazonSqsBrokerFaultTests),
                nameof(AmazonSqsBrokerFaultTests.should_resume_delivery_once_after_consumer_pause)
            ),
            new(
                TransportConformanceScenario.BrokerInterruptionRecovery,
                typeof(AmazonSqsBrokerFaultTests),
                nameof(AmazonSqsBrokerFaultTests.should_resume_delivery_once_after_broker_restart)
            ),
            _Bind(
                TransportConformanceScenario.StaleSettlement,
                nameof(
                    AmazonSqsConsumerClientConformanceTests.should_not_settle_another_delivery_when_a_settled_value_is_used_again
                )
            ),
            _Bind(
                TransportConformanceScenario.HandlerFailureRedelivery,
                nameof(
                    AmazonSqsConsumerClientConformanceTests.should_keep_the_delivery_and_the_listener_when_the_callback_throws
                )
            ),
            _Bind(
                TransportConformanceScenario.BoundedGracefulShutdown,
                nameof(AmazonSqsConsumerClientConformanceTests.should_shutdown_idle_consumer_within_bound)
            ),
            _Bind(
                TransportConformanceScenario.BusConsumerIdentityFanOut,
                nameof(
                    AmazonSqsConsumerClientConformanceTests.should_deliver_one_bus_copy_per_consumer_identity_while_replicas_compete
                )
            ),
            _Bind(
                TransportConformanceScenario.BusReplicaCompetition,
                nameof(
                    AmazonSqsConsumerClientConformanceTests.should_deliver_one_bus_copy_per_consumer_identity_while_replicas_compete
                )
            ),
            _Bind(
                TransportConformanceScenario.QueueOwnership,
                nameof(AmazonSqsConsumerClientConformanceTests.should_deliver_one_owned_queue_copy_across_replicas)
            ),
            _Bind(
                TransportConformanceScenario.SameNameLaneIsolation,
                nameof(AmazonSqsConsumerClientConformanceTests.should_isolate_same_logical_name_between_bus_and_queue)
            ),
            new(
                TransportConformanceScenario.MalformedEnvelopeTerminalSettlement,
                typeof(MalformedMessageTests),
                nameof(MalformedMessageTests.should_reject_message_with_invalid_json)
            ),
            new(
                TransportConformanceScenario.RequestReplyStartupRejection,
                typeof(ProviderConformanceEvidenceTests),
                nameof(should_reject_requests_and_responders_at_startup_on_a_transport_without_request_reply)
            ),
            _Bind(
                TransportConformanceScenario.StandardHeaderRoundTrip,
                nameof(AmazonSqsConsumerClientConformanceTests.should_round_trip_every_standard_header_unchanged)
            ),
            _Bind(
                TransportConformanceScenario.HandlerConcurrencyCap,
                nameof(
                    AmazonSqsConsumerClientConformanceTests.should_never_hand_over_more_deliveries_at_once_than_the_requested_concurrency
                )
            ),
            _Bind(
                TransportConformanceScenario.PerKeyOrdering,
                nameof(AmazonSqsConsumerClientConformanceTests.should_deliver_the_messages_of_one_key_in_publish_order)
            ),
            _Bind(
                TransportConformanceScenario.BodySizeLimit,
                nameof(
                    AmazonSqsConsumerClientConformanceTests.should_round_trip_a_body_at_the_size_limit_and_refuse_one_byte_more
                )
            ),
        ];

        TransportConformanceTestBindings.GetValidationErrors(profile, bindings).Should().BeEmpty();
    }

    private static TransportConformanceTestBinding _Bind(TransportConformanceScenario scenario, string method) =>
        new(scenario, typeof(AmazonSqsConsumerClientConformanceTests), method);
}
