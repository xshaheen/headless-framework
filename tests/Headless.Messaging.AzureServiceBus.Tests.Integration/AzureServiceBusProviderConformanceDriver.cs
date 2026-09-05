// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Azure.Messaging.ServiceBus;
using Azure.Messaging.ServiceBus.Administration;
using Headless.Messaging;
using Headless.Messaging.AzureServiceBus.Producer;
using Tests.Capabilities;

namespace Tests;

internal sealed class AzureServiceBusProviderConformanceDriver(AzureServiceBusFixture fixture)
    : TransportProviderConformanceDriver
{
    private static readonly TransportConformanceProfile _Profile = TransportConformanceManifest.Providers[
        "Azure Service Bus"
    ];
    private string? _topicName;

    public override string ProviderName => _Profile.Provider;

    public override bool SupportsRoutingAffinity => true;

    public override void ConfigureRoutingAffinityTransport(
        Headless.Messaging.Configuration.MessagingSetupBuilder setup
    ) =>
        setup.UseAzureServiceBus(options =>
        {
            options.ConnectionString = fixture.ConnectionString;
            options.EnableSessions = true;
            options.AutoProvision = false;
        });

    public override async Task AssertNativePublisherPathsAsync(CancellationToken cancellationToken)
    {
        var topic = await fixture.CreateTopicAsync(cancellationToken);
        var subscription = $"affinity-{Guid.NewGuid():N}";
        var destination = $"custom-affinity-{Guid.NewGuid():N}";
        var administration = new ServiceBusAdministrationClient(fixture.ConnectionString);
        await administration.CreateSubscriptionAsync(
            new CreateSubscriptionOptions(topic, subscription) { RequiresSession = true },
            cancellationToken
        );
        await using var client = new ServiceBusClient(fixture.ConnectionString);
        ServiceBusSessionReceiver? receiver = null;
        try
        {
            await TransportRoutingAffinityConformance.AssertPublisherPathsAsync(
                setup =>
                    setup.UseAzureServiceBus(options =>
                    {
                        options.ConnectionString = fixture.ConnectionString;
                        options.AutoProvision = false;
                        options.EnableSessions = false;
                        options.CustomProducers.Add(
                            new ServiceBusProducerDescriptor(
                                destination,
                                topic,
                                createSubscription: false,
                                enableSessions: true
                            )
                        );
                    }),
                destination,
                async (expectedId, token) =>
                {
                    receiver ??= await client.AcceptSessionAsync(
                        topic,
                        subscription,
                        "order-42",
                        cancellationToken: token
                    );
                    var native = await receiver.ReceiveMessageAsync(TimeSpan.FromSeconds(30), token);
                    native.Should().NotBeNull();
                    native!.SessionId.Should().Be("order-42");
                    native.MessageId.Should().Be(expectedId);
                    native.ApplicationProperties[Headers.RoutingAffinityKey].Should().Be("order-42");
                    await receiver.CompleteMessageAsync(native, token);
                },
                cancellationToken,
                lane: MessageLane.Bus
            );
        }
        finally
        {
            if (receiver is not null)
            {
                await receiver.DisposeAsync();
            }
        }
    }

    public override ValueTask<TransportConsumerConformanceSession> CreateRoutingAffinitySessionAsync(
        TransportConformanceEndpoint endpoint,
        CancellationToken cancellationToken
    ) => fixture.CreateRoutingAffinitySessionAsync(endpoint.LogicalName, endpoint.SubscriberGroup, cancellationToken);

    public override void AssertNativeRoutingAffinity(TransportConformanceDelivery delivery, string expectedKey) =>
        delivery.Message.Headers["conformance-native-session"].Should().Be(expectedKey);

    public override string? GetNativeRoutingPlacement(TransportConformanceDelivery delivery) =>
        delivery.Message.Headers["conformance-native-session"];

    public override TransportMalformedEnvelopeBound MalformedEnvelopeBound => _Profile.MalformedEnvelopeBound!;

    public override async ValueTask<TransportConsumerConformanceSession> CreateSessionAsync(
        TransportConformanceEndpoint endpoint,
        CancellationToken cancellationToken
    )
    {
        var topicName = string.Empty;
        if (endpoint.Lane == MessageLane.Bus)
        {
            _topicName ??= await fixture.CreateTopicAsync(cancellationToken);
            topicName = _topicName;
        }
        return await fixture.CreateConformanceSessionAsync(
            endpoint.Lane,
            endpoint.LogicalName,
            endpoint.SubscriberGroup,
            topicName,
            ownsEntity: string.Equals(endpoint.Replica, "replica-1", StringComparison.Ordinal),
            cancellationToken
        );
    }
}
