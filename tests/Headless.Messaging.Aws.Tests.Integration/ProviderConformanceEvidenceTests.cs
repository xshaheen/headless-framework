// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Testing.Tests;
using Tests.Capabilities;

namespace Tests;

[Collection<LocalStackTestFixture>]
public sealed class ProviderConformanceEvidenceTests(LocalStackTestFixture fixture) : TestBase
{
    [Fact]
    public async Task should_execute_every_supported_manifest_scenario()
    {
        var profile = TransportConformanceManifest.Providers["AWS/LocalStack"];
        TransportConformanceTestBinding[] bindings =
        [
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
            _Bind(
                TransportConformanceScenario.BoundedGracefulShutdown,
                nameof(AmazonSqsConsumerClientConformanceTests.should_shutdown_idle_consumer_within_bound)
            ),
            _Bind(
                TransportConformanceScenario.BusSubscriberGroupFanOut,
                nameof(
                    AmazonSqsConsumerClientConformanceTests.should_deliver_one_bus_copy_per_group_while_replicas_compete
                )
            ),
            _Bind(
                TransportConformanceScenario.BusReplicaCompetition,
                nameof(
                    AmazonSqsConsumerClientConformanceTests.should_deliver_one_bus_copy_per_group_while_replicas_compete
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
        ];

        await TransportConformanceTestBindings.ExecuteSupportedScenariosAsync(profile, bindings, _CreateTestClass);
    }

    private static TransportConformanceTestBinding _Bind(TransportConformanceScenario scenario, string method) =>
        new(scenario, typeof(AmazonSqsConsumerClientConformanceTests), method);

    private object _CreateTestClass(Type testClass)
    {
        if (testClass == typeof(AmazonSqsConsumerClientConformanceTests))
        {
            return new AmazonSqsConsumerClientConformanceTests(fixture);
        }

        if (testClass == typeof(MalformedMessageTests))
        {
            return new MalformedMessageTests(fixture);
        }

        throw new InvalidOperationException($"No AWS conformance test factory is registered for {testClass}.");
    }
}
