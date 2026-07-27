// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Messaging;
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
