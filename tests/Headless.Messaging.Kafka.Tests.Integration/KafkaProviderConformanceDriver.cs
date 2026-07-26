// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Messaging;
using Tests.Capabilities;

namespace Tests;

internal sealed class KafkaProviderConformanceDriver(KafkaFixture fixture) : TransportProviderConformanceDriver
{
    private static readonly TransportConformanceProfile _Profile = TransportConformanceManifest.Providers["Kafka"];

    public override string ProviderName => _Profile.Provider;

    public override TransportConformanceDriverCapabilities Capabilities { get; } =
        new(false, false, false, false, false);

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
            endpoint.SubscriberGroup,
            createReplacement: false
        );
    }
}
