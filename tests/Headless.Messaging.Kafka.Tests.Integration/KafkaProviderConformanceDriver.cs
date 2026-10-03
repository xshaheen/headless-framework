// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Messaging;
using Tests.Capabilities;

namespace Tests;

internal sealed class KafkaProviderConformanceDriver(KafkaFixture fixture) : TransportProviderConformanceDriver
{
    private static readonly TransportConformanceProfile _Profile = TransportConformanceManifest.Providers["Kafka"];

    public override string ProviderName => _Profile.Provider;

    public override bool SupportsRoutingAffinity => true;

    public override void ConfigureRoutingAffinityTransport(
        Headless.Messaging.Configuration.MessagingSetupBuilder setup
    ) => setup.UseKafka(fixture.ConnectionString);

    // The same transport a consuming host uses, so the startup-rejection proof runs against the real provider wiring.
    public override void ConfigureRequestReplyTransport(Headless.Messaging.Configuration.MessagingSetupBuilder setup) =>
        ConfigureRoutingAffinityTransport(setup);

    public override void AssertNativeRoutingAffinity(TransportConformanceDelivery delivery, string expectedKey)
    {
        var native = delivery
            .SettlementValue.Should()
            .BeOfType<Headless.Messaging.Kafka.KafkaConsumerClient.KafkaDelivery>()
            .Subject;
        native.ConsumerResult.Message.Key.Should().Be(expectedKey);
    }

    public override string GetNativeRoutingPlacement(TransportConformanceDelivery delivery) =>
        (
            (Headless.Messaging.Kafka.KafkaConsumerClient.KafkaDelivery)delivery.SettlementValue!
        ).ConsumerResult.Partition.Value.ToString(CultureInfo.InvariantCulture);

    public override TransportMalformedEnvelopeBound MalformedEnvelopeBound => _Profile.MalformedEnvelopeBound!;

    public override ValueTask<TransportConsumerConformanceSession> CreateSessionAsync(
        TransportConformanceEndpoint endpoint,
        CancellationToken cancellationToken
    )
    {
        if (endpoint.Lane != MessageLane.Queue)
        {
            throw new NotSupportedException("Kafka conformance sessions support only the Queue lane.");
        }

        return fixture.CreateConformanceSessionAsync(
            cancellationToken,
            endpoint.LogicalName,
            endpoint.SubscriptionName,
            createReplacement: false
        );
    }
}
