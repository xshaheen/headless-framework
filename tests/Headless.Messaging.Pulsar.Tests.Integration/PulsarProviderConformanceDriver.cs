// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Messaging;
using Headless.Messaging.Pulsar;
using Microsoft.Extensions.DependencyInjection;
using Pulsar.Client.Common;
using Tests.Capabilities;

namespace Tests;

internal sealed class PulsarProviderConformanceDriver(PulsarFixture fixture) : TransportProviderConformanceDriver
{
    private static readonly TransportConformanceProfile _Profile = TransportConformanceManifest.Providers["Pulsar"];

    public override string ProviderName => _Profile.Provider;

    public override bool SupportsRoutingAffinity => true;

    public override void ConfigureRoutingAffinityTransport(
        Headless.Messaging.Configuration.MessagingSetupBuilder setup
    ) => setup.UsePulsar(fixture.ConnectionString);

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
        var client = await provider.GetRequiredService<IConnectionFactory>().RentClientAsync(cancellationToken);
        await using var consumer = await client
            .NewConsumer()
            .Topic(PulsarPhysicalAddress.Topic(lane, destination))
            .SubscriptionName($"native-{Guid.NewGuid():N}")
            .SubscriptionInitialPosition(SubscriptionInitialPosition.Earliest)
            .SubscribeAsync();

        await TransportRoutingAffinityConformance.AssertPublisherPathsAsync(
            ConfigureRoutingAffinityTransport,
            destination,
            async (expectedId, token) =>
            {
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
                timeout.CancelAfter(TimeSpan.FromSeconds(30));
                var native = await consumer.ReceiveAsync(timeout.Token);
                native.Key.Should().Be("order-42");
                native.Properties[Headers.MessageId].Should().Be(expectedId);
                native.Properties[Headers.RoutingAffinityKey].Should().Be("order-42");
                await consumer.AcknowledgeAsync(native.MessageId);
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
        return fixture.CreateLaneSessionAsync(
            endpoint.Lane,
            endpoint.LogicalName,
            endpoint.SubscriberGroup,
            cancellationToken
        );
    }
}
