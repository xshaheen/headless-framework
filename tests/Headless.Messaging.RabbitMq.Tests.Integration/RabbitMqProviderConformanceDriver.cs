// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Messaging;
using Tests.Capabilities;

namespace Tests;

internal sealed class RabbitMqProviderConformanceDriver(RabbitMqFixture fixture) : TransportProviderConformanceDriver
{
    private static readonly TransportConformanceProfile _Profile = TransportConformanceManifest.Providers["RabbitMQ"];
    private readonly string _exchangeName = $"conformance-{Guid.NewGuid():N}";

    public override string ProviderName => _Profile.Provider;

    public override TransportMalformedEnvelopeBound MalformedEnvelopeBound => _Profile.MalformedEnvelopeBound!;

    public override ValueTask<TransportConsumerConformanceSession> CreateSessionAsync(
        TransportConformanceEndpoint endpoint,
        CancellationToken cancellationToken
    )
    {
        return fixture.CreateLaneSessionAsync(
            endpoint.Lane,
            _exchangeName,
            endpoint.LogicalName,
            endpoint.SubscriberGroup,
            cancellationToken
        );
    }
}
