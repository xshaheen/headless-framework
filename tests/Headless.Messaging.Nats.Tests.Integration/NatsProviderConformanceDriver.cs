// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Messaging.Transport;
using Tests.Capabilities;

namespace Tests;

internal sealed class NatsProviderConformanceDriver(NatsFixture fixture) : TransportProviderConformanceDriver
{
    private static readonly TransportConformanceProfile _Profile = TransportConformanceManifest.Providers["NATS"];
    private readonly string _streamName = $"conformance-{Guid.NewGuid():N}"[..30];

    public override string ProviderName => _Profile.Provider;

    public override TransportMalformedEnvelopeBound MalformedEnvelopeBound => _Profile.MalformedEnvelopeBound!;

    public override bool SupportsEveryInstance => true;

    public override ValueTask<TransportConsumerConformanceSession> CreateSessionAsync(
        TransportConformanceEndpoint endpoint,
        CancellationToken cancellationToken
    )
    {
        if (endpoint.Kind is ConsumerSubscriptionKind.EveryInstance)
        {
            return fixture.CreateEndpointSessionAsync(endpoint, _streamName, cancellationToken);
        }

        return fixture.CreateLaneSessionAsync(
            endpoint.Lane,
            _streamName,
            endpoint.LogicalName,
            endpoint.SubscriptionName,
            cancellationToken
        );
    }
}
