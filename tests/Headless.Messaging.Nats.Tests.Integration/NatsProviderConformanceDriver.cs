// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Tests.Capabilities;

namespace Tests;

internal sealed class NatsProviderConformanceDriver(NatsFixture fixture) : TransportProviderConformanceDriver
{
    private static readonly TransportConformanceProfile _Profile = TransportConformanceManifest.Providers["NATS"];
    private readonly string _streamName = $"conformance-{Guid.NewGuid():N}"[..30];

    public override string ProviderName => _Profile.Provider;

    public override TransportMalformedEnvelopeBound MalformedEnvelopeBound => _Profile.MalformedEnvelopeBound!;

    public override ValueTask<TransportConsumerConformanceSession> CreateSessionAsync(
        TransportConformanceEndpoint endpoint,
        CancellationToken cancellationToken
    )
    {
        return fixture.CreateLaneSessionAsync(
            endpoint.Lane,
            _streamName,
            endpoint.LogicalName,
            endpoint.SubscriberGroup,
            cancellationToken
        );
    }
}
