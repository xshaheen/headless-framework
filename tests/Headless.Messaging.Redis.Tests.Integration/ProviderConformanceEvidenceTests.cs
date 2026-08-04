// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Testing.Tests;
using Tests.Capabilities;

namespace Tests;

[Collection<RedisMessagingFixture>]
public sealed class ProviderConformanceEvidenceTests(RedisMessagingFixture fixture) : TestBase
{
    [Fact]
    public async Task should_execute_every_supported_manifest_scenario()
    {
        var profile = TransportConformanceManifest.Providers["Redis"];
        TransportConformanceTestBinding[] bindings =
        [
            _Bind(
                TransportConformanceScenario.QueueRoundTrip,
                nameof(RedisConsumerConformanceTests.should_round_trip_queue_message_body_and_headers)
            ),
            _Bind(
                TransportConformanceScenario.BusRoundTrip,
                nameof(RedisConsumerConformanceTests.should_deliver_one_bus_copy_per_group_while_replicas_compete)
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
                TransportConformanceScenario.BusSubscriberGroupFanOut,
                nameof(RedisConsumerConformanceTests.should_deliver_one_bus_copy_per_group_while_replicas_compete)
            ),
            _Bind(
                TransportConformanceScenario.BusReplicaCompetition,
                nameof(RedisConsumerConformanceTests.should_deliver_one_bus_copy_per_group_while_replicas_compete)
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
            _Bind(
                TransportConformanceScenario.LegacyCutoverRecovery,
                nameof(
                    RedisConsumerConformanceTests.should_roll_forward_legacy_stream_without_deleting_operator_owned_source
                )
            ),
        ];

        await TransportConformanceTestBindings.ExecuteSupportedScenariosAsync(
            profile,
            bindings,
            testClass =>
                testClass == typeof(RedisConsumerConformanceTests)
                    ? new RedisConsumerConformanceTests(fixture)
                    : throw new InvalidOperationException(
                        $"No Redis conformance test factory is registered for {testClass}."
                    )
        );
    }

    private static TransportConformanceTestBinding _Bind(TransportConformanceScenario scenario, string method) =>
        new(scenario, typeof(RedisConsumerConformanceTests), method);
}
