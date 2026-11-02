// Copyright (c) Mahmoud Shaheen. All rights reserved.

using DotPulsar;
using DotPulsar.Extensions;
using Headless.Messaging;
using Headless.Messaging.Pulsar;
using Microsoft.Extensions.DependencyInjection;
using Tests.Capabilities;

namespace Tests;

internal sealed class PulsarProviderConformanceDriver(PulsarFixture fixture) : TransportProviderConformanceDriver
{
    private static readonly TransportConformanceProfile _Profile = TransportConformanceManifest.Providers["Pulsar"];

    public override string ProviderName => _Profile.Provider;

    public override bool SupportsRoutingAffinity => true;

    public override bool SupportsEveryInstance => true;

    public override void ConfigureRoutingAffinityTransport(Headless.Messaging.MessagingSetupBuilder setup) =>
        setup.UsePulsar(fixture.ConnectionString);

    // The same transport a consuming host uses, so the startup-rejection proof runs against the real provider wiring.
    public override void ConfigureRequestReplyTransport(Headless.Messaging.MessagingSetupBuilder setup) =>
        ConfigureRoutingAffinityTransport(setup);

    public override async Task AssertNativePublisherPathsAsync(CancellationToken cancellationToken)
    {
        await _AssertNativePublisherPathsAsync(MessageLane.Queue, cancellationToken);
        await _AssertNativePublisherPathsAsync(MessageLane.Bus, cancellationToken);
    }

    private async Task _AssertNativePublisherPathsAsync(MessageLane lane, CancellationToken cancellationToken)
    {
        var destination = $"native-affinity-{Guid.NewGuid():N}";
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddHeadlessMessaging(ConfigureRoutingAffinityTransport);
        await using var provider = services.BuildServiceProvider();
        var client = provider.GetRequiredService<IConnectionFactory>().RentClient();
        await using var consumer = client
            .NewConsumer()
            .Topic(PulsarPhysicalAddress.Topic(lane, destination))
            .SubscriptionName($"native-{Guid.NewGuid():N}")
            .InitialPosition(SubscriptionInitialPosition.Earliest)
            .Create();
        await consumer.OnStateChangeTo(ConsumerState.Active, TimeSpan.FromSeconds(30), cancellationToken);

        await TransportRoutingAffinityConformance.AssertPublisherPathsAsync(
            ConfigureRoutingAffinityTransport,
            destination,
            async (expectedId, token) =>
            {
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
                timeout.CancelAfter(TimeSpan.FromSeconds(30));
                var native = await consumer.Receive(timeout.Token);
                native.Key.Should().Be("order-42");
                native.Properties[Headers.MessageId].Should().Be(expectedId);
                native.Properties[Headers.RoutingAffinityKey].Should().Be("order-42");
                await consumer.Acknowledge(native.MessageId, token);
            },
            cancellationToken,
            lane
        );
    }

    public override TransportMalformedEnvelopeBound MalformedEnvelopeBound => _Profile.MalformedEnvelopeBound!;

    public override ValueTask<TransportConsumerConformanceSession> CreateSessionAsync(
        TransportConformanceEndpoint endpoint,
        CancellationToken cancellationToken
    )
    {
        return fixture.CreateEndpointSessionAsync(endpoint, cancellationToken);
    }
}
