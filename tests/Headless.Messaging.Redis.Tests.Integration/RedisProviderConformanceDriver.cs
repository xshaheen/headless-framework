// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Tests.Capabilities;

namespace Tests;

internal sealed class RedisProviderConformanceDriver(RedisMessagingFixture fixture) : TransportProviderConformanceDriver
{
    private static readonly TransportConformanceProfile _Profile = TransportConformanceManifest.Providers["Redis"];

    public override string ProviderName => _Profile.Provider;

    public override TransportConformanceDriverCapabilities Capabilities { get; } =
        new(false, false, false, false, false);

    public override TransportMalformedEnvelopeBound MalformedEnvelopeBound => _Profile.MalformedEnvelopeBound!;

    public override ValueTask<TransportConsumerConformanceSession> CreateSessionAsync(
        TransportConformanceEndpoint endpoint,
        CancellationToken cancellationToken
    )
    {
        return fixture.CreateSessionAsync(
            endpoint.Lane,
            endpoint.LogicalName,
            endpoint.SubscriberGroup,
            cancellationToken,
            ownsStream: string.Equals(endpoint.Replica, "replica-1", StringComparison.Ordinal)
        );
    }
}
