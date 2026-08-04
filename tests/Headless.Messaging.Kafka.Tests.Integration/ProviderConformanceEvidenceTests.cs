// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Messaging;
using Headless.Messaging.Configuration;
using Headless.Messaging.Internal;
using Headless.Messaging.Persistence;
using Headless.Testing.Tests;
using Microsoft.Extensions.DependencyInjection;
using Tests.Capabilities;

namespace Tests;

[Collection("Kafka")]
public sealed class ProviderConformanceEvidenceTests(KafkaFixture fixture) : TestBase
{
    [Fact]
    public async Task should_execute_every_supported_manifest_scenario()
    {
        var profile = TransportConformanceManifest.Providers["Kafka"];
        TransportConformanceTestBinding[] bindings =
        [
            _Bind(
                TransportConformanceScenario.QueueRoundTrip,
                nameof(KafkaConsumerClientConformanceTests.should_round_trip_queue_message_body_and_headers)
            ),
            _Bind(
                TransportConformanceScenario.HeaderRoundTrip,
                nameof(KafkaConsumerClientConformanceTests.should_round_trip_queue_message_body_and_headers)
            ),
            _Bind(
                TransportConformanceScenario.CommitSettlement,
                nameof(KafkaConsumerClientConformanceTests.should_commit_real_delivery_and_prevent_redelivery)
            ),
            _Bind(
                TransportConformanceScenario.RejectRedelivery,
                nameof(KafkaConsumerClientConformanceTests.should_reject_real_delivery_and_observe_redelivery)
            ),
            new(
                TransportConformanceScenario.ConsumerPauseRecovery,
                typeof(KafkaBrokerFaultTests),
                nameof(KafkaBrokerFaultTests.should_resume_delivery_once_after_consumer_pause)
            ),
            _Bind(
                TransportConformanceScenario.BoundedGracefulShutdown,
                nameof(KafkaConsumerClientConformanceTests.should_shutdown_idle_consumer_within_bound)
            ),
            _Bind(
                TransportConformanceScenario.QueueOwnership,
                nameof(KafkaConsumerClientConformanceTests.should_deliver_one_owned_queue_copy_across_replicas)
            ),
            new(
                TransportConformanceScenario.StartupRejectionBeforeSideEffects,
                typeof(ProviderConformanceEvidenceTests),
                nameof(should_reject_bus_route_before_storage_or_broker_side_effects)
            ),
            _Bind(
                TransportConformanceScenario.MalformedEnvelopeTerminalSettlement,
                nameof(
                    KafkaConsumerClientConformanceTests.should_terminally_commit_missing_required_headers_across_consumer_restart
                )
            ),
        ];

        await TransportConformanceTestBindings.ExecuteSupportedScenariosAsync(profile, bindings, _CreateTestClass);
    }

    [Fact]
    public async Task should_reject_bus_route_before_storage_or_broker_side_effects()
    {
        var storageInitializeCalls = 0;
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddHeadlessMessaging(options =>
        {
            options.UseKafka("localhost:9092");
            options.Bus.ForMessage<KafkaBusContract>(message => message.MessageName("orders.changed"));
        });
        services.AddMessagingProviderCapabilities(
            MessagingProviderCapabilities.Storage(
                "TestStorage",
                [MessageLane.Bus, MessageLane.Queue],
                supportsDelayedScheduling: true
            )
        );
        services.AddSingleton<IStorageInitializer>(new RecordingStorageInitializer(() => storageInitializeCalls++));

        await using var provider = services.BuildServiceProvider();
        var act = () => provider.GetRequiredService<IBootstrapper>().BootstrapAsync(AbortToken);

        await act.Should()
            .ThrowAsync<MessagingConfigurationException>()
            .WithMessage("*Kafka*does not support Bus*Supported lanes: Queue*setup.Queue.ForMessage*");
        storageInitializeCalls.Should().Be(0);
    }

    private static TransportConformanceTestBinding _Bind(TransportConformanceScenario scenario, string method) =>
        new(scenario, typeof(KafkaConsumerClientConformanceTests), method);

    private object _CreateTestClass(Type testClass)
    {
        if (testClass == typeof(KafkaConsumerClientConformanceTests))
        {
            return new KafkaConsumerClientConformanceTests(fixture);
        }

        if (testClass == typeof(KafkaBrokerFaultTests))
        {
            return new KafkaBrokerFaultTests(fixture);
        }

        if (testClass == typeof(ProviderConformanceEvidenceTests))
        {
            return new ProviderConformanceEvidenceTests(fixture);
        }

        throw new InvalidOperationException($"No Kafka conformance test factory is registered for {testClass}.");
    }

    private sealed record KafkaBusContract;

    private sealed class RecordingStorageInitializer(Action initialize) : IStorageInitializer
    {
        public Task InitializeAsync(CancellationToken cancellationToken = default)
        {
            initialize();
            return Task.CompletedTask;
        }

        public string GetPublishedTableName() => "published";

        public string GetReceivedTableName() => "received";
    }
}
